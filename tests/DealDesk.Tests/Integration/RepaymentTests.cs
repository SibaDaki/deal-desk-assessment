using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace DealDesk.Tests.Integration
{
    public class RepaymentTests : ApiTestBase
    {
        public RepaymentTests(DealDeskApiFactory factory) : base(factory)
        {
        }

        [Fact] // Acceptance scenario 9
        public async Task Funding_computes_the_advance_from_the_approved_terms()
        {
            var id = await CreateFundedDeal();

            var deal = await GetDeal(id);
            Assert.Equal("FUNDED", deal["status"]!.GetValue<string>());
            Assert.Equal(45_000_000, deal["funded_amount_cents"]!.GetValue<long>());
            Assert.Equal("2026-07-01", deal["funded_on"]!.GetValue<string>());
        }

        [Fact] // Acceptance scenario 10
        public async Task Statement_shows_exact_fee_maths_for_two_started_periods()
        {
            // funded 45_000_000 at 350 bps, 2026-07-01 -> 2026-08-30 = 60 days = 2 periods
            var id = await CreateFundedDeal(fundedOn: "2026-07-01");

            var statement = await GetStatement(id);
            Assert.Equal(45_000_000, statement["funded_amount_cents"]!.GetValue<long>());
            Assert.Equal(3_150_000, statement["total_fee_cents"]!.GetValue<long>());
            Assert.Equal(48_150_000, statement["total_repayable_cents"]!.GetValue<long>());
            Assert.Equal(0, statement["total_repaid_cents"]!.GetValue<long>());
            Assert.Equal(48_150_000, statement["outstanding_cents"]!.GetValue<long>());
        }

        [Fact] // Acceptance scenario 11
        public async Task Repaying_the_full_amount_settles_the_deal()
        {
            var id = await CreateFundedDeal();

            var partial = await Analyst.PostAsJsonAsync($"/api/deals/{id}/repayments",
                new { amount_cents = 40_000_000 });
            Assert.Equal(HttpStatusCode.Created, partial.StatusCode);
            Assert.Equal(8_150_000, (await Body(partial))["outstanding_cents"]!.GetValue<long>());

            var final = await Analyst.PostAsJsonAsync($"/api/deals/{id}/repayments",
                new { amount_cents = 8_150_000 });
            Assert.Equal(HttpStatusCode.Created, final.StatusCode);

            var deal = await GetDeal(id);
            Assert.Equal("SETTLED", deal["status"]!.GetValue<string>());

            var statement = await GetStatement(id);
            Assert.Equal(0, statement["outstanding_cents"]!.GetValue<long>());
            Assert.Equal(48_150_000, statement["total_repaid_cents"]!.GetValue<long>());
        }

        [Fact] // Acceptance scenario 12
        public async Task Overpayment_is_rejected_and_the_balance_is_unchanged()
        {
            var id = await CreateFundedDeal();

            var response = await Analyst.PostAsJsonAsync($"/api/deals/{id}/repayments",
                new { amount_cents = 48_150_001 });

            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            var error = (await Body(response))["error"]!;
            Assert.Equal(48_150_000, error["outstanding_cents"]!.GetValue<long>());

            var statement = await GetStatement(id);
            Assert.Equal(48_150_000, statement["outstanding_cents"]!.GetValue<long>());
        }

        [Fact]
        public async Task Repayments_against_non_funded_deals_are_rejected_with_422()
        {
            var deal = await CreateDeal();
            var id = deal["id"]!.GetValue<string>();

            var response = await Analyst.PostAsJsonAsync($"/api/deals/{id}/repayments",
                new { amount_cents = 1_000_000 });

            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        }

        [Fact]
        public async Task Non_positive_repayments_are_rejected_with_422()
        {
            var id = await CreateFundedDeal();

            var zero = await Analyst.PostAsJsonAsync($"/api/deals/{id}/repayments", new { amount_cents = 0 });
            Assert.Equal(HttpStatusCode.UnprocessableEntity, zero.StatusCode);

            var missing = await Analyst.PostAsJsonAsync($"/api/deals/{id}/repayments", new { });
            Assert.Equal(HttpStatusCode.UnprocessableEntity, missing.StatusCode);
        }

        [Fact]
        public async Task Settled_deals_accept_no_further_repayments()
        {
            var id = await CreateFundedDeal();
            await Analyst.PostAsJsonAsync($"/api/deals/{id}/repayments", new { amount_cents = 48_150_000 });

            var extra = await Analyst.PostAsJsonAsync($"/api/deals/{id}/repayments", new { amount_cents = 1 });

            Assert.Equal(HttpStatusCode.UnprocessableEntity, extra.StatusCode);
        }

        [Fact]
        public async Task Statement_for_unknown_deal_returns_404()
        {
            var response = await Applicant.GetAsync($"/api/deals/{Guid.NewGuid()}/statement");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }
}
