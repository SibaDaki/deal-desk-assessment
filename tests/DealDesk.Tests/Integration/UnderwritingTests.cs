using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace DealDesk.Tests.Integration
{
    public class UnderwritingTests : ApiTestBase
    {
        public UnderwritingTests(DealDeskApiFactory factory) : base(factory)
        {
        }

        [Fact] // Acceptance scenario 4
        public async Task Funding_a_submitted_deal_is_a_conflict_and_leaves_status_unchanged()
        {
            var deal = await CreateDeal();
            var id = deal["id"]!.GetValue<string>();

            var response = await Analyst.PostAsJsonAsync($"/api/deals/{id}/fund", new { funded_on = "2026-07-01" });

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("SUBMITTED", (await GetDeal(id))["status"]!.GetValue<string>());
        }

        [Fact]
        public async Task Fund_conflict_wins_even_when_the_body_is_empty()
        {
            var deal = await CreateDeal();
            var id = deal["id"]!.GetValue<string>();

            var response = await Analyst.PostAsJsonAsync($"/api/deals/{id}/fund", new { });

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }

        [Fact] // Acceptance scenario 5
        public async Task Analyst_actions_require_the_analyst_role()
        {
            var deal = await CreateDeal();
            var id = deal["id"]!.GetValue<string>();

            var asApplicant = await Applicant.PostAsync($"/api/deals/{id}/review", null);
            Assert.Equal(HttpStatusCode.Forbidden, asApplicant.StatusCode);

            var withoutRole = await Anonymous.PostAsync($"/api/deals/{id}/review", null);
            Assert.Equal(HttpStatusCode.Forbidden, withoutRole.StatusCode);

            Assert.Equal("SUBMITTED", (await GetDeal(id))["status"]!.GetValue<string>());

            foreach (var action in new[] { "approve", "decline", "fund", "repayments" })
            {
                var response = await Applicant.PostAsJsonAsync($"/api/deals/{id}/{action}", new { });
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            }
        }

        [Fact] // Acceptance scenario 6
        public async Task Approve_is_blocked_while_required_documents_are_missing()
        {
            var deal = await CreateDeal();
            var id = deal["id"]!.GetValue<string>();
            await AttachDocument(id, "company_registration");

            var review = await Analyst.PostAsync($"/api/deals/{id}/review", null);
            Assert.Equal(HttpStatusCode.OK, review.StatusCode);

            var approve = await Analyst.PostAsJsonAsync($"/api/deals/{id}/approve", new
            {
                advance_rate_bps = 9000,
                facility_fee_rate_bps = 350,
                expected_settlement_date = "2026-08-30"
            });

            Assert.Equal(HttpStatusCode.UnprocessableEntity, approve.StatusCode);
            var error = (await Body(approve))["error"]!;
            Assert.Equal(new[] { "purchase_order" }, StringArray(error["missing_documents"]));
            Assert.Equal("UNDER_REVIEW", (await GetDeal(id))["status"]!.GetValue<string>());
        }

        [Fact] // Acceptance scenario 7
        public async Task Large_deals_additionally_require_financial_statements()
        {
            var deal = await CreateDeal(150_000_000);
            var id = deal["id"]!.GetValue<string>();
            await AttachDocument(id, "company_registration");
            await AttachDocument(id, "purchase_order");
            await Analyst.PostAsync($"/api/deals/{id}/review", null);

            var approve = await Analyst.PostAsJsonAsync($"/api/deals/{id}/approve", new
            {
                advance_rate_bps = 9000,
                facility_fee_rate_bps = 350,
                expected_settlement_date = "2026-08-30"
            });

            Assert.Equal(HttpStatusCode.UnprocessableEntity, approve.StatusCode);
            var error = (await Body(approve))["error"]!;
            Assert.Equal(new[] { "financial_statements" }, StringArray(error["missing_documents"]));
        }

        [Fact] // Acceptance scenario 8
        public async Task Happy_path_underwrite_persists_terms()
        {
            var id = await CreateApprovedDeal();

            var deal = await GetDeal(id);
            Assert.Equal("APPROVED", deal["status"]!.GetValue<string>());
            Assert.Equal(9000, deal["terms"]!["advance_rate_bps"]!.GetValue<int>());
            Assert.Equal(350, deal["terms"]!["facility_fee_rate_bps"]!.GetValue<int>());
            Assert.Equal("2026-08-30", deal["terms"]!["expected_settlement_date"]!.GetValue<string>());
        }

        [Fact]
        public async Task Invalid_document_type_is_rejected()
        {
            var deal = await CreateDeal();
            var id = deal["id"]!.GetValue<string>();

            var response = await Applicant.PostAsJsonAsync($"/api/deals/{id}/documents", new
            {
                doc_type = "passport_scan",
                filename = "passport.pdf"
            });

            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        }

        [Fact]
        public async Task Decline_records_the_reason_and_is_terminal()
        {
            var deal = await CreateDeal();
            var id = deal["id"]!.GetValue<string>();

            var decline = await Analyst.PostAsJsonAsync($"/api/deals/{id}/decline",
                new { reason = "Buyer credit risk too high" });
            Assert.Equal(HttpStatusCode.OK, decline.StatusCode);

            var declined = await GetDeal(id);
            Assert.Equal("DECLINED", declined["status"]!.GetValue<string>());
            Assert.Equal("Buyer credit risk too high", declined["decline_reason"]!.GetValue<string>());

            var review = await Analyst.PostAsync($"/api/deals/{id}/review", null);
            Assert.Equal(HttpStatusCode.Conflict, review.StatusCode);
        }

        [Fact]
        public async Task Approve_rejects_invalid_terms_with_422()
        {
            var deal = await CreateDeal();
            var id = deal["id"]!.GetValue<string>();
            await AttachDocument(id, "company_registration");
            await AttachDocument(id, "purchase_order");
            await Analyst.PostAsync($"/api/deals/{id}/review", null);

            var badAdvance = await Analyst.PostAsJsonAsync($"/api/deals/{id}/approve", new
            {
                advance_rate_bps = 0,
                facility_fee_rate_bps = 350,
                expected_settlement_date = "2026-08-30"
            });
            Assert.Equal(HttpStatusCode.UnprocessableEntity, badAdvance.StatusCode);

            var pastDate = await Analyst.PostAsJsonAsync($"/api/deals/{id}/approve", new
            {
                advance_rate_bps = 9000,
                facility_fee_rate_bps = 350,
                expected_settlement_date = "2026-05-01" // before the fixed "today" of 2026-06-01
            });
            Assert.Equal(HttpStatusCode.UnprocessableEntity, pastDate.StatusCode);

            Assert.Equal("UNDER_REVIEW", (await GetDeal(id))["status"]!.GetValue<string>());
        }

        [Fact]
        public async Task Audit_trail_captures_transitions_with_actor_and_timestamp()
        {
            var id = await CreateApprovedDeal();

            var deal = await GetDeal(id);
            var actions = deal["audit_trail"]!.AsArray()
                .Select(a => a!["action"]!.GetValue<string>())
                .ToArray();

            Assert.Equal(new[] { "create", "review", "approve" }, actions);
            Assert.Equal("analyst",
                deal["audit_trail"]!.AsArray()[2]!["actor_role"]!.GetValue<string>());
        }
    }
}
