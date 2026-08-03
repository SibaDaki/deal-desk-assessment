using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace DealDesk.Tests.Integration
{
    /// <summary>
    /// Verifies that the provided seed-deals.json is loaded at startup, that the
    /// seeded deals are fully functional aggregates (not just display data), and
    /// that new references continue after the seeded sequence.
    /// </summary>
    public class SeedDataTests : ApiTestBase
    {
        public SeedDataTests(DealDeskApiFactory factory) : base(factory)
        {
        }

        private async Task<JsonNode?> FindByReference(string reference)
        {
            var deals = await Body(await Applicant.GetAsync("/api/deals"));
            return deals.AsArray()
                .SingleOrDefault(d => d!["reference"]!.GetValue<string>() == reference);
        }

        [Fact]
        public async Task Seeded_deals_are_loaded_with_their_statuses_and_documents()
        {
            var submitted = await FindByReference("SF-2026-0001");
            Assert.NotNull(submitted);
            Assert.Equal("SUBMITTED", submitted!["status"]!.GetValue<string>());
            Assert.Equal("Kagiso Trading & Projects (Pty) Ltd", submitted["applicant_name"]!.GetValue<string>());
            Assert.Equal(82_000_000, submitted["deal_amount_cents"]!.GetValue<long>());
            Assert.Equal(2, submitted["documents"]!.AsArray().Count);

            var approved = await FindByReference("SF-2026-0003");
            Assert.NotNull(approved);
            Assert.Equal("APPROVED", approved!["status"]!.GetValue<string>());
            Assert.Equal(9000, approved["terms"]!["advance_rate_bps"]!.GetValue<int>());
            Assert.Equal("2026-09-01", approved["terms"]!["expected_settlement_date"]!.GetValue<string>());

            var declined = await FindByReference("SF-2026-0004");
            Assert.NotNull(declined);
            Assert.Equal("DECLINED", declined!["status"]!.GetValue<string>());
            Assert.Equal("Buyer credit risk too high", declined["decline_reason"]!.GetValue<string>());
        }

        [Fact]
        public async Task New_deals_continue_the_reference_sequence_after_the_seeds()
        {
            var deal = await CreateDeal(applicantName: "After The Seeds Ltd");
            var reference = deal["reference"]!.GetValue<string>();

            var sequence = int.Parse(reference.Split('-')[2]);
            Assert.True(sequence >= 5, $"Expected a sequence after the 4 seeded deals but got {reference}");
        }

        [Fact]
        public async Task Seeded_deal_under_review_can_be_approved_through_the_normal_flow()
        {
            // SF-2026-0002 is seeded UNDER_REVIEW with its invoice and company
            // registration attached, so approval should work directly.
            var seeded = await FindByReference("SF-2026-0002");
            Assert.NotNull(seeded);
            var id = seeded!["id"]!.GetValue<string>();

            var approve = await Analyst.PostAsJsonAsync($"/api/deals/{id}/approve", new
            {
                advance_rate_bps = 8000,
                facility_fee_rate_bps = 300,
                expected_settlement_date = "2026-08-30"
            });

            Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
            Assert.Equal("APPROVED", (await Body(approve))["status"]!.GetValue<string>());
        }

        [Fact]
        public async Task Seeded_declined_deal_is_terminal()
        {
            var declined = await FindByReference("SF-2026-0004");
            var id = declined!["id"]!.GetValue<string>();

            var review = await Analyst.PostAsync($"/api/deals/{id}/review", null);

            Assert.Equal(HttpStatusCode.Conflict, review.StatusCode);
        }
    }
}
