using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace DealDesk.Tests.Integration
{
    public class DealIntakeTests : ApiTestBase
    {
        public DealIntakeTests(DealDeskApiFactory factory) : base(factory)
        {
        }

        [Fact] // Acceptance scenario 1
        public async Task Valid_intake_creates_a_submitted_deal_with_a_unique_reference()
        {
            var first = await CreateDeal(50_000_000);
            var second = await CreateDeal(50_000_000);

            Assert.Equal("SUBMITTED", first["status"]!.GetValue<string>());
            Assert.Matches(new Regex(@"^SF-\d{4}-\d{4,}$"), first["reference"]!.GetValue<string>());
            Assert.NotEqual(first["reference"]!.GetValue<string>(), second["reference"]!.GetValue<string>());
            Assert.Equal(50_000_000, first["deal_amount_cents"]!.GetValue<long>());
        }

        [Fact] // Acceptance scenario 2
        public async Task Below_minimum_amount_is_rejected_and_no_deal_is_created()
        {
            var response = await Applicant.PostAsJsonAsync("/api/deals", new
            {
                applicant_name = "Under Minimum Ltd",
                funding_type = "purchase_order",
                deal_amount_cents = 24_999_999,
                buyer_name = "Buyer",
                buyer_sector = "corporate"
            });

            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

            var list = await Applicant.GetAsync("/api/deals");
            var deals = await Body(list);
            Assert.DoesNotContain(deals.AsArray(),
                d => d!["applicant_name"]!.GetValue<string>() == "Under Minimum Ltd");
        }

        [Fact]
        public async Task Applicant_actions_require_the_applicant_role()
        {
            var body = new
            {
                applicant_name = "Wrong Role Ltd",
                funding_type = "purchase_order",
                deal_amount_cents = 50_000_000,
                buyer_name = "Buyer",
                buyer_sector = "corporate"
            };

            var asAnalyst = await Analyst.PostAsJsonAsync("/api/deals", body);
            Assert.Equal(HttpStatusCode.Forbidden, asAnalyst.StatusCode);

            var withoutRole = await Anonymous.PostAsJsonAsync("/api/deals", body);
            Assert.Equal(HttpStatusCode.Forbidden, withoutRole.StatusCode);

            var list = await Applicant.GetAsync("/api/deals");
            Assert.DoesNotContain((await Body(list)).AsArray(),
                d => d!["applicant_name"]!.GetValue<string>() == "Wrong Role Ltd");

            var deal = await CreateDeal();
            var id = deal["id"]!.GetValue<string>();

            var document = new { doc_type = "company_registration", filename = "cipc.pdf" };
            var docAsAnalyst = await Analyst.PostAsJsonAsync($"/api/deals/{id}/documents", document);
            Assert.Equal(HttpStatusCode.Forbidden, docAsAnalyst.StatusCode);

            var docWithoutRole = await Anonymous.PostAsJsonAsync($"/api/deals/{id}/documents", document);
            Assert.Equal(HttpStatusCode.Forbidden, docWithoutRole.StatusCode);

            Assert.Empty((await GetDeal(id))["documents"]!.AsArray());
        }

        [Fact] // Acceptance scenario 3
        public async Task Invalid_funding_type_is_rejected()
        {
            var response = await Applicant.PostAsJsonAsync("/api/deals", new
            {
                applicant_name = "Bridging Ltd",
                funding_type = "bridging",
                deal_amount_cents = 50_000_000,
                buyer_name = "Buyer",
                buyer_sector = "corporate"
            });

            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        }

        [Fact]
        public async Task Invalid_buyer_sector_and_missing_fields_are_rejected()
        {
            var badSector = await Applicant.PostAsJsonAsync("/api/deals", new
            {
                applicant_name = "Sector Ltd",
                funding_type = "purchase_order",
                deal_amount_cents = 50_000_000,
                buyer_name = "Buyer",
                buyer_sector = "municipal"
            });
            Assert.Equal(HttpStatusCode.UnprocessableEntity, badSector.StatusCode);

            var missingFields = await Applicant.PostAsJsonAsync("/api/deals", new { });
            Assert.Equal(HttpStatusCode.UnprocessableEntity, missingFields.StatusCode);
        }

        [Fact]
        public async Task Unknown_deal_returns_404()
        {
            var response = await Applicant.GetAsync($"/api/deals/{Guid.NewGuid()}");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task List_filters_by_status_and_funding_type()
        {
            var poDeal = await CreateDeal(fundingType: "purchase_order");
            var invoiceDeal = await CreateDeal(fundingType: "invoice_discounting");
            var poId = poDeal["id"]!.GetValue<string>();
            var invoiceId = invoiceDeal["id"]!.GetValue<string>();

            var byType = await Body(await Applicant.GetAsync("/api/deals?funding_type=invoice_discounting"));
            Assert.Contains(byType.AsArray(), d => d!["id"]!.GetValue<string>() == invoiceId);
            Assert.DoesNotContain(byType.AsArray(), d => d!["id"]!.GetValue<string>() == poId);

            var review = await Analyst.PostAsync($"/api/deals/{poId}/review", null);
            Assert.Equal(HttpStatusCode.OK, review.StatusCode);

            var byStatus = await Body(await Applicant.GetAsync("/api/deals?status=UNDER_REVIEW"));
            Assert.Contains(byStatus.AsArray(), d => d!["id"]!.GetValue<string>() == poId);
            Assert.All(byStatus.AsArray(), d => Assert.Equal("UNDER_REVIEW", d!["status"]!.GetValue<string>()));

            var badFilter = await Applicant.GetAsync("/api/deals?status=NOT_A_STATUS");
            Assert.Equal(HttpStatusCode.UnprocessableEntity, badFilter.StatusCode);
        }

        [Fact] // Bonus: pagination
        public async Task List_supports_pagination()
        {
            for (var i = 0; i < 3; i++)
                await CreateDeal(applicantName: $"Paged Applicant {i}");

            var response = await Applicant.GetAsync("/api/deals?page=1&page_size=2");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var items = await Body(response);
            Assert.Equal(2, items.AsArray().Count);
            Assert.True(int.Parse(response.Headers.GetValues("X-Total-Count").Single()) >= 3);
        }

        [Fact] // Bonus: idempotency
        public async Task Idempotency_key_prevents_duplicate_deals()
        {
            var key = Guid.NewGuid().ToString();

            using var first = new HttpRequestMessage(HttpMethod.Post, "/api/deals")
            {
                Content = JsonContent.Create(new
                {
                    applicant_name = "Idempotent Ltd",
                    funding_type = "purchase_order",
                    deal_amount_cents = 50_000_000,
                    buyer_name = "Buyer",
                    buyer_sector = "corporate"
                })
            };
            first.Headers.Add("Idempotency-Key", key);
            var firstResponse = await Applicant.SendAsync(first);
            Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
            var firstDeal = await Body(firstResponse);

            using var second = new HttpRequestMessage(HttpMethod.Post, "/api/deals")
            {
                Content = JsonContent.Create(new
                {
                    applicant_name = "Idempotent Ltd",
                    funding_type = "purchase_order",
                    deal_amount_cents = 50_000_000,
                    buyer_name = "Buyer",
                    buyer_sector = "corporate"
                })
            };
            second.Headers.Add("Idempotency-Key", key);
            var secondResponse = await Applicant.SendAsync(second);
            Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);
            var secondDeal = await Body(secondResponse);

            Assert.Equal(firstDeal["id"]!.GetValue<string>(), secondDeal["id"]!.GetValue<string>());
            Assert.Equal("true", secondResponse.Headers.GetValues("Idempotency-Replayed").Single());
        }
    }
}
