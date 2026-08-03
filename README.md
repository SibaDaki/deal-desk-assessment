# Deal Desk API — Purchase Order Funding Lifecycle

A REST API that moves a funding deal from application through underwriting,
disbursement and repayment: the Sourcefin deal lifecycle with a strict state
machine, document gating, role-based actions and deterministic integer-cents
money maths.

## Stack

- .NET 8 / ASP.NET Core Web API
- Clean Architecture: `Domain` → `Application` → `Infrastructure` → `Api`
- JWT Bearer authentication (register/login, PBKDF2 password hashing, role claims)
- In-memory persistence (allowed by the brief) behind repository interfaces
- xUnit test suite: domain unit tests + full-stack integration tests via `WebApplicationFactory`

## Install, run, test

```bash
# requires the .NET 8 SDK (https://dotnet.microsoft.com/download/dotnet/8.0)
dotnet build            # build everything
dotnet test             # run the whole suite (unit + integration)
dotnet run --project dealdesk/DealDesk.Api   # start the API (Swagger UI at /swagger in Development)
```

Quick sanity check once running:

```bash
curl -X POST http://localhost:5278/api/deals \
  -H "Content-Type: application/json" -H "X-Actor-Role: applicant" \
  -d '{"applicant_name":"Thandi Trading","funding_type":"purchase_order","deal_amount_cents":50000000,"buyer_name":"Transnet","buyer_sector":"soe"}'
```

## Project layout

| Project | Responsibility |
| --- | --- |
| `DealDesk.Domain` | The business rules, with no framework dependencies: the `Deal` aggregate, state machine, fee maths (`MoneyMath`, `FeeSchedule`), document gating, typed `Result`/`Error` |
| `DealDesk.Application` | Orchestration: `DealService`, request/response DTOs, wire mapping, ports (`IDealRepository`, `IClock`, `IReferenceGenerator`, `IIdempotencyStore`) |
| `DealDesk.Infrastructure` | Adapters: in-memory repository, sequential `SF-{year}-{seq}` reference generator, idempotency store, system clock |
| `DealDesk.Api` | Transport only: controllers, the `X-Actor-Role` gate, error → HTTP status mapping, snake_case JSON |
| `DealDesk.Tests` | Unit tests for the domain rules and integration tests covering all twelve acceptance scenarios |

The domain layer never sees HTTP. Controllers translate typed errors to
status codes: `Validation → 422`, `NotFound → 404`, `Conflict → 409`,
`Forbidden → 403`.

## Domain rules

### State machine

```
SUBMITTED ──review──▶ UNDER_REVIEW ──approve──▶ APPROVED ──fund──▶ FUNDED ──(fully repaid)──▶ SETTLED
    │                      │
    └──────decline─────────┴──────────▶ DECLINED
```

Legal transitions are declared once in `DealStateMachine`; anything else is a
409 and leaves the deal untouched. `SETTLED` and `DECLINED` are terminal.
Settlement is automatic when the outstanding balance reaches zero.

### Money and rounding

All amounts are integer ZAR cents (`long`) and all rates are integer basis
points; no floats or decimals anywhere in the money path. The rounding rule is
**round half up**, implemented in pure integer arithmetic:

```
apply_bps(amount, bps) = (amount * bps + 5000) / 10000   // integer division
```

Fees accrue per **started** 30-day period, minimum one period, and are fixed at
funding time:

```
periods              = max(1, ceil(days_between(funded_on, expected_settlement_date) / 30))
funded_amount_cents  = apply_bps(deal_amount_cents, advance_rate_bps)
total_fee_cents      = apply_bps(funded_amount_cents, facility_fee_rate_bps) * periods
total_repayable_cents = funded_amount_cents + total_fee_cents
```

Note the order: the per-period fee is rounded first, then multiplied by the
period count (covered by an explicit unit test).

### Document gating (checked at approval)

- Always: `company_registration`
- `purchase_order` deals: `purchase_order`; `invoice_discounting` deals: `invoice`
- Deals strictly over R1,000,000 (`deal_amount_cents > 100000000`): `financial_statements`

A blocked approval returns 422 with the missing types, e.g.
`{"error":{"code":"missing_documents","missing_documents":["purchase_order"],...}}`.

### Authentication and roles

Every action is gated to the actor the spec assigns it: creating a deal and
uploading documents are applicant-only, while `review`, `approve`, `decline`,
`fund` and `repayments` are analyst-only. A wrong role gets 403 before any
other rule runs; reads are open to any caller.

The caller's role is established one of two ways:

1. **JWT Bearer token (proper auth).** `POST /api/auth/register` creates a user
   (`username`, `password`, `role` of `applicant`/`analyst`; passwords stored
   as salted PBKDF2-SHA256 hashes) and `POST /api/auth/login` exchanges
   credentials for an HMAC-SHA256-signed JWT whose `role` claim drives the
   gates. Send it as `Authorization: Bearer <access_token>`; Swagger has an
   Authorize button for it. Two demo users are seeded at startup:
   `demo-applicant` / `Applicant#2026` and `demo-analyst` / `Analyst#2026`
   (configurable under `Auth:SeedUsers`). The signing key in appsettings.json
   is dev-only — production overrides `Jwt__SigningKey`. A presented-but-invalid
   token is 401 and never falls back to the header.
2. **`X-Actor-Role` header (assessment contract).** Unauthenticated callers may
   assert `applicant` or `analyst` via the header, exactly as the brief
   specifies, so all acceptance scenarios run unchanged. Set
   `Auth:AllowActorRoleHeader` to `false` to turn this off; every write then
   requires a Bearer token (401 without one).

## API summary

| Endpoint | Role | Success | Errors |
| --- | --- | --- | --- |
| `POST /api/auth/register` | any | 201 | 422 |
| `POST /api/auth/login` | any | 200 | 401, 422 |
| `POST /api/deals` | applicant | 201 | 403, 422 |
| `GET /api/deals?status=&funding_type=&page=&page_size=` | any | 200 | 422 (bad filter/paging) |
| `GET /api/deals/{id}` | any | 200 | 404 |
| `POST /api/deals/{id}/documents` | applicant | 201 | 403, 404, 422 |
| `POST /api/deals/{id}/review` | analyst | 200 | 403, 404, 409 |
| `POST /api/deals/{id}/approve` | analyst | 200 | 403, 404, 409, 422 |
| `POST /api/deals/{id}/decline` | analyst | 200 | 403, 404, 409, 422 (missing reason) |
| `POST /api/deals/{id}/fund` | analyst | 200 | 403, 404, 409, 422 |
| `POST /api/deals/{id}/repayments` | analyst | 201 | 403, 404, 422 |
| `GET /api/deals/{id}/statement` | any | 200 | 404 |

Error precedence per request: **401 → 403 → 404 → 409 → 422**, so e.g. funding
a `SUBMITTED` deal returns 409 even if the body is also invalid.

## Seed data

`seed-deals.json` (the sample data provided with the assessment) sits at the
solution root and is loaded into the store on startup: four deals covering
`SUBMITTED`, `UNDER_REVIEW`, `APPROVED` (with terms) and `DECLINED`. Seeded
deals are materialized through `Deal.Restore`, so they obey the same invariants
and money maths as deals created through the API and can continue their
lifecycle normally (e.g. the seeded `UNDER_REVIEW` deal can be approved). The
reference generator is advanced past the seeded sequence, so the next created
deal becomes `SF-2026-0005`. Set the `SeedDataPath` configuration key to point
elsewhere, or remove the file to start empty; a malformed seed file fails
startup deliberately.

## Bonus features implemented

- **JWT authentication**: register/login endpoints, PBKDF2-SHA256 password
  hashing, signed Bearer tokens with role claims, seeded demo users, and a
  config switch to disable the spec's header fallback entirely.
- **Idempotency** on `POST /api/deals` via the `Idempotency-Key` header —
  replays return the originally created deal (with an `Idempotency-Replayed: true` header) instead of creating a duplicate.
- **Pagination** on the deal list via `?page=` and `?page_size=` (response body
  stays a plain array; totals come back in `X-Total-Count` / `X-Page` headers).
- **Audit trail**: every status transition is recorded with action, from/to
  status, actor role and timestamp, and returned as `audit_trail` on the deal.

## Testing

`dotnet test` runs 79 tests:

- **Unit** (`tests/DealDesk.Tests/Unit`): rounding behaviour, period counting
  boundaries (30/31/60/61 days, same-day minimum), document threshold at exactly
  vs. above R1m, and the full aggregate state machine including exact-amount
  assertions for funding, settlement and overpayment.
- **Integration** (`tests/DealDesk.Tests/Integration`): all twelve acceptance
  scenarios from the brief end-to-end over HTTP, plus role gates on every
  endpoint, the full JWT flow (register, login, wrong-role 403, invalid-token
  401, header fallback off), filters, pagination and idempotency. The clock is
  injected (`IClock`) and pinned to 2026-06-01 in tests — token lifetimes are
  validated against the same clock — so date rules are deterministic.

## Assumptions and decisions

- **Persistence** is in-memory, as the brief allows. All storage sits behind
  `IDealRepository`/`IReferenceGenerator`/`IIdempotencyStore`, so a PostgreSQL +
  EF Core implementation (as in my other work) slots in by swapping the
  `Program.cs` registrations.
- Test fixtures are derived from the acceptance scenarios in the brief; the
  provided `seed-deals.json` is additionally loaded at startup (and under test,
  where dedicated tests assert the seeded deals' statuses and behaviour).
- `expected_settlement_date` is validated at approval as strictly after
  *today* (funding hasn't happened yet); at funding, `funded_on` must be
  strictly before the agreed settlement date.
- Declining without a `reason` returns 422 (the field is documented as
  required even though the contract table doesn't list a 422 for decline).
- The statement endpoint returns zeros for deals that have not been funded yet
  (the contract allows only 200/404 for it).
- Documents may be attached in any status; they are only *checked* at
  approval. Repeated uploads of the same type are allowed (e.g. replacing a
  bank statement) — the gate only needs at least one of each required type.
- Create and document upload are applicant-only, mirroring the analyst gate on
  the underwriting actions, so every write is tied to the actor the spec
  assigns it. Read endpoints stay open to any caller.
