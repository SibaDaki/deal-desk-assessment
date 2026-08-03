using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace DealDesk.Tests.Integration
{
    public abstract class ApiTestBase : IClassFixture<DealDeskApiFactory>
    {
        protected ApiTestBase(DealDeskApiFactory factory)
        {
            Applicant = factory.CreateClient();
            Applicant.DefaultRequestHeaders.Add("X-Actor-Role", "applicant");

            Analyst = factory.CreateClient();
            Analyst.DefaultRequestHeaders.Add("X-Actor-Role", "analyst");

            Anonymous = factory.CreateClient();
        }

        protected HttpClient Applicant { get; }
        protected HttpClient Analyst { get; }
        protected HttpClient Anonymous { get; }

        protected static async Task<JsonNode> Body(HttpResponseMessage response)
        {
            var text = await response.Content.ReadAsStringAsync();
            return JsonNode.Parse(text)!;
        }

        protected async Task<JsonNode> CreateDeal(
            long amountCents = 50_000_000,
            string fundingType = "purchase_order",
            string buyerSector = "government",
            string applicantName = "Thandi Trading (Pty) Ltd",
            string buyerName = "Dept of Public Works")
        {
            var response = await Applicant.PostAsJsonAsync("/api/deals", new
            {
                applicant_name = applicantName,
                funding_type = fundingType,
                deal_amount_cents = amountCents,
                buyer_name = buyerName,
                buyer_sector = buyerSector
            });

            Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
            return await Body(response);
        }

        protected async Task AttachDocument(string dealId, string docType)
        {
            var response = await Applicant.PostAsJsonAsync($"/api/deals/{dealId}/documents", new
            {
                doc_type = docType,
                filename = $"{docType}.pdf"
            });

            Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
        }

        /// <summary>Creates a deal, attaches all required docs, reviews and approves it.</summary>
        protected async Task<string> CreateApprovedDeal(
            long amountCents = 50_000_000,
            int advanceRateBps = 9000,
            int facilityFeeRateBps = 350,
            string expectedSettlementDate = "2026-08-30")
        {
            var deal = await CreateDeal(amountCents);
            var id = deal["id"]!.GetValue<string>();

            await AttachDocument(id, "company_registration");
            await AttachDocument(id, "purchase_order");
            if (amountCents > 100_000_000)
                await AttachDocument(id, "financial_statements");

            var review = await Analyst.PostAsync($"/api/deals/{id}/review", null);
            Assert.Equal(System.Net.HttpStatusCode.OK, review.StatusCode);

            var approve = await Analyst.PostAsJsonAsync($"/api/deals/{id}/approve", new
            {
                advance_rate_bps = advanceRateBps,
                facility_fee_rate_bps = facilityFeeRateBps,
                expected_settlement_date = expectedSettlementDate
            });
            Assert.Equal(System.Net.HttpStatusCode.OK, approve.StatusCode);

            return id;
        }

        /// <summary>Creates an approved deal and funds it on the given date.</summary>
        protected async Task<string> CreateFundedDeal(string fundedOn = "2026-07-01")
        {
            var id = await CreateApprovedDeal();
            var fund = await Analyst.PostAsJsonAsync($"/api/deals/{id}/fund", new { funded_on = fundedOn });
            Assert.Equal(System.Net.HttpStatusCode.OK, fund.StatusCode);
            return id;
        }

        protected async Task<JsonNode> GetDeal(string id)
        {
            var response = await Applicant.GetAsync($"/api/deals/{id}");
            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
            return await Body(response);
        }

        protected async Task<JsonNode> GetStatement(string id)
        {
            var response = await Applicant.GetAsync($"/api/deals/{id}/statement");
            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
            return await Body(response);
        }

        protected static string[] StringArray(JsonNode? node) =>
            node!.AsArray().Select(n => n!.GetValue<string>()).ToArray();
    }
}
