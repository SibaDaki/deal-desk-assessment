using DealDesk.Application.Abstractions;
using DealDesk.Application.DTOs.Requests;
using DealDesk.Application.DTOs.Responses;
using DealDesk.Application.Mapping;
using DealDesk.Domain.Common;
using DealDesk.Domain.Entities;
using DealDesk.Domain.Enums;
using DealDesk.Domain.Services;
using Microsoft.Extensions.Logging;

namespace DealDesk.Application.Services
{
    /// <summary>
    /// Application service orchestrating the deal lifecycle. Input parsing and
    /// request-shape validation happen here; the business rules themselves
    /// (state machine, document gating, money maths) live on the Deal aggregate.
    ///
    /// Error-precedence contract, applied per endpoint:
    /// 404 (unknown deal) → 409 (illegal transition) → 422 (invalid body).
    /// Mutations are applied inside a per-deal lock so concurrent repayments
    /// cannot race the settlement check on the in-memory store.
    /// </summary>
    public sealed class DealService : IDealService
    {
        private readonly IDealRepository _deals;
        private readonly IReferenceGenerator _references;
        private readonly IIdempotencyStore _idempotency;
        private readonly IClock _clock;
        private readonly ILogger<DealService> _logger;

        public DealService(
            IDealRepository deals,
            IReferenceGenerator references,
            IIdempotencyStore idempotency,
            IClock clock,
            ILogger<DealService> logger)
        {
            _deals = deals;
            _references = references;
            _idempotency = idempotency;
            _clock = clock;
            _logger = logger;
        }

        public async Task<Result<CreateDealOutcome>> CreateAsync(
            CreateDealRequest request, string actorRole, string? idempotencyKey,
            CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrWhiteSpace(idempotencyKey) &&
                _idempotency.TryGet(idempotencyKey, out var existingId))
            {
                var existing = await _deals.GetByIdAsync(existingId, cancellationToken);
                if (existing is not null)
                {
                    _logger.LogInformation(
                        "Replaying idempotent create for key {IdempotencyKey} -> deal {DealId}",
                        idempotencyKey, existingId);
                    return Result.Success(new CreateDealOutcome(DealMapper.ToResponse(existing), Replayed: true));
                }
            }

            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(request.ApplicantName))
                errors.Add("applicant_name is required.");
            if (string.IsNullOrWhiteSpace(request.BuyerName))
                errors.Add("buyer_name is required.");

            FundingType fundingType = default;
            if (string.IsNullOrWhiteSpace(request.FundingType))
                errors.Add("funding_type is required.");
            else if (!Wire.TryParseFundingType(request.FundingType, out fundingType))
                errors.Add($"funding_type must be one of: {string.Join(", ", Wire.FundingTypeValues)}.");

            BuyerSector buyerSector = default;
            if (string.IsNullOrWhiteSpace(request.BuyerSector))
                errors.Add("buyer_sector is required.");
            else if (!Wire.TryParseBuyerSector(request.BuyerSector, out buyerSector))
                errors.Add($"buyer_sector must be one of: {string.Join(", ", Wire.BuyerSectorValues)}.");

            if (request.DealAmountCents is null)
                errors.Add("deal_amount_cents is required.");
            else if (request.DealAmountCents < Deal.MinimumDealAmountCents)
                errors.Add($"deal_amount_cents must be at least {Deal.MinimumDealAmountCents} (R250,000).");

            if (errors.Count > 0)
                return Result.Failure<CreateDealOutcome>(Error.Validation("Deal intake failed validation.", errors));

            var reference = _references.NextReference(_clock.Today.Year);
            var deal = Deal.Create(
                reference,
                request.ApplicantName!.Trim(),
                fundingType,
                request.DealAmountCents!.Value,
                request.BuyerName!.Trim(),
                buyerSector,
                actorRole,
                _clock.UtcNow);

            await _deals.AddAsync(deal, cancellationToken);

            if (!string.IsNullOrWhiteSpace(idempotencyKey))
                _idempotency.Put(idempotencyKey, deal.Id);

            _logger.LogInformation("Created deal {Reference} ({DealId}) for {Applicant}",
                deal.Reference, deal.Id, deal.ApplicantName);

            return Result.Success(new CreateDealOutcome(DealMapper.ToResponse(deal), Replayed: false));
        }

        public async Task<Result<PagedDeals>> ListAsync(
            string? status, string? fundingType, int? page, int? pageSize,
            CancellationToken cancellationToken = default)
        {
            DealStatus? statusFilter = null;
            if (!string.IsNullOrWhiteSpace(status))
            {
                if (!Wire.TryParseStatus(status, out var parsed))
                    return Result.Failure<PagedDeals>(Error.Validation($"Unknown status filter '{status}'."));
                statusFilter = parsed;
            }

            FundingType? fundingTypeFilter = null;
            if (!string.IsNullOrWhiteSpace(fundingType))
            {
                if (!Wire.TryParseFundingType(fundingType, out var parsed))
                    return Result.Failure<PagedDeals>(Error.Validation($"Unknown funding_type filter '{fundingType}'."));
                fundingTypeFilter = parsed;
            }

            if (page is < 1)
                return Result.Failure<PagedDeals>(Error.Validation("page must be 1 or greater."));
            if (pageSize is < 1 or > 100)
                return Result.Failure<PagedDeals>(Error.Validation("page_size must be between 1 and 100."));

            var deals = await _deals.ListAsync(statusFilter, fundingTypeFilter, cancellationToken);
            var totalCount = deals.Count;

            IEnumerable<Deal> window = deals;
            if (page is not null || pageSize is not null)
            {
                var effectivePage = page ?? 1;
                var effectiveSize = pageSize ?? 20;
                window = deals.Skip((effectivePage - 1) * effectiveSize).Take(effectiveSize);
                return Result.Success(new PagedDeals(
                    window.Select(DealMapper.ToResponse).ToList(), totalCount, effectivePage, effectiveSize));
            }

            return Result.Success(new PagedDeals(
                deals.Select(DealMapper.ToResponse).ToList(), totalCount, null, null));
        }

        public async Task<Result<DealResponse>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var deal = await _deals.GetByIdAsync(id, cancellationToken);
            return deal is null
                ? Result.Failure<DealResponse>(Error.NotFound())
                : Result.Success(DealMapper.ToResponse(deal));
        }

        public async Task<Result<DealResponse>> AttachDocumentAsync(
            Guid id, AttachDocumentRequest request, CancellationToken cancellationToken = default)
        {
            var deal = await _deals.GetByIdAsync(id, cancellationToken);
            if (deal is null)
                return Result.Failure<DealResponse>(Error.NotFound());

            if (string.IsNullOrWhiteSpace(request.DocType))
                return Result.Failure<DealResponse>(Error.Validation("doc_type is required."));

            if (!Wire.TryParseDocumentType(request.DocType, out var docType))
                return Result.Failure<DealResponse>(Error.Validation(
                    $"doc_type must be one of: {string.Join(", ", Wire.DocumentTypeValues)}."));

            Result attach;
            lock (deal)
            {
                attach = deal.AttachDocument(docType, request.Filename ?? string.Empty);
            }

            if (attach.IsFailure)
                return Result.Failure<DealResponse>(attach.Error!);

            await _deals.UpdateAsync(deal, cancellationToken);
            return Result.Success(DealMapper.ToResponse(deal));
        }

        public Task<Result<DealResponse>> ReviewAsync(
            Guid id, string actorRole, CancellationToken cancellationToken = default) =>
            MutateAsync(id, deal => deal.StartReview(actorRole, _clock.UtcNow), cancellationToken);

        public Task<Result<DealResponse>> ApproveAsync(
            Guid id, ApproveDealRequest request, string actorRole, CancellationToken cancellationToken = default) =>
            MutateAsync(id, deal =>
            {
                // Check the transition first so an illegal state yields 409 even
                // when the body is also invalid or missing.
                var allowed = DealStateMachine.EnsureCanApply(deal.Status, DealAction.Approve);
                if (allowed.IsFailure)
                    return allowed;

                var errors = new List<string>();
                if (request.AdvanceRateBps is null)
                    errors.Add("advance_rate_bps is required.");
                if (request.FacilityFeeRateBps is null)
                    errors.Add("facility_fee_rate_bps is required.");

                DateOnly settlementDate = default;
                if (string.IsNullOrWhiteSpace(request.ExpectedSettlementDate))
                    errors.Add("expected_settlement_date is required.");
                else if (!DealMapper.TryParseIsoDate(request.ExpectedSettlementDate, out settlementDate))
                    errors.Add("expected_settlement_date must be an ISO date (yyyy-MM-dd).");

                if (errors.Count > 0)
                    return Result.Failure(Error.Validation("Approval terms are invalid.", errors));

                return deal.Approve(
                    request.AdvanceRateBps!.Value,
                    request.FacilityFeeRateBps!.Value,
                    settlementDate,
                    _clock.Today,
                    actorRole,
                    _clock.UtcNow);
            }, cancellationToken);

        public Task<Result<DealResponse>> DeclineAsync(
            Guid id, DeclineDealRequest request, string actorRole, CancellationToken cancellationToken = default) =>
            MutateAsync(id, deal => deal.Decline(request.Reason, actorRole, _clock.UtcNow), cancellationToken);

        public Task<Result<DealResponse>> FundAsync(
            Guid id, FundDealRequest request, string actorRole, CancellationToken cancellationToken = default) =>
            MutateAsync(id, deal =>
            {
                var allowed = DealStateMachine.EnsureCanApply(deal.Status, DealAction.Fund);
                if (allowed.IsFailure)
                    return allowed;

                if (string.IsNullOrWhiteSpace(request.FundedOn))
                    return Result.Failure(Error.Validation("funded_on is required."));

                if (!DealMapper.TryParseIsoDate(request.FundedOn, out var fundedOn))
                    return Result.Failure(Error.Validation("funded_on must be an ISO date (yyyy-MM-dd)."));

                return deal.Fund(fundedOn, actorRole, _clock.UtcNow);
            }, cancellationToken);

        public async Task<Result<RepaymentResponse>> RecordRepaymentAsync(
            Guid id, RecordRepaymentRequest request, string actorRole, CancellationToken cancellationToken = default)
        {
            var deal = await _deals.GetByIdAsync(id, cancellationToken);
            if (deal is null)
                return Result.Failure<RepaymentResponse>(Error.NotFound());

            if (request.AmountCents is null)
                return Result.Failure<RepaymentResponse>(Error.Validation("amount_cents is required."));

            Result<Repayment> recorded;
            lock (deal)
            {
                recorded = deal.RecordRepayment(request.AmountCents.Value, actorRole, _clock.UtcNow);
            }

            if (recorded.IsFailure)
                return Result.Failure<RepaymentResponse>(recorded.Error!);

            await _deals.UpdateAsync(deal, cancellationToken);

            _logger.LogInformation(
                "Recorded repayment of {AmountCents} cents on deal {Reference}; outstanding {OutstandingCents}",
                request.AmountCents.Value, deal.Reference, deal.OutstandingCents);

            return Result.Success(new RepaymentResponse(
                recorded.Value.Id,
                deal.Id,
                recorded.Value.AmountCents,
                recorded.Value.RecordedAt,
                deal.Status.ToWire(),
                deal.OutstandingCents));
        }

        public async Task<Result<StatementResponse>> GetStatementAsync(
            Guid id, CancellationToken cancellationToken = default)
        {
            var deal = await _deals.GetByIdAsync(id, cancellationToken);
            return deal is null
                ? Result.Failure<StatementResponse>(Error.NotFound())
                : Result.Success(DealMapper.ToStatement(deal));
        }

        private async Task<Result<DealResponse>> MutateAsync(
            Guid id, Func<Deal, Result> mutation, CancellationToken cancellationToken)
        {
            var deal = await _deals.GetByIdAsync(id, cancellationToken);
            if (deal is null)
                return Result.Failure<DealResponse>(Error.NotFound());

            Result result;
            lock (deal)
            {
                result = mutation(deal);
            }

            if (result.IsFailure)
                return Result.Failure<DealResponse>(result.Error!);

            await _deals.UpdateAsync(deal, cancellationToken);
            return Result.Success(DealMapper.ToResponse(deal));
        }
    }
}
