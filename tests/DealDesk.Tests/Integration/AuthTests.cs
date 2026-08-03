using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Xunit;

namespace DealDesk.Tests.Integration
{
    public class AuthTests : ApiTestBase
    {
        private readonly DealDeskApiFactory _factory;

        public AuthTests(DealDeskApiFactory factory) : base(factory)
        {
            _factory = factory;
        }

        private async Task<string> Login(string username, string password)
        {
            var response = await Anonymous.PostAsJsonAsync("/api/auth/login", new
            {
                username,
                password
            });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await Body(response))["access_token"]!.GetValue<string>();
        }

        private HttpClient BearerClient(string token)
        {
            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return client;
        }

        [Fact]
        public async Task Seeded_users_can_login_and_drive_the_lifecycle_with_bearer_tokens_only()
        {
            var applicant = BearerClient(await Login("demo-applicant", "Applicant#2026"));
            var analyst = BearerClient(await Login("demo-analyst", "Analyst#2026"));

            var create = await applicant.PostAsJsonAsync("/api/deals", new
            {
                applicant_name = "Token Trading (Pty) Ltd",
                funding_type = "purchase_order",
                deal_amount_cents = 50_000_000,
                buyer_name = "Buyer",
                buyer_sector = "corporate"
            });
            Assert.Equal(HttpStatusCode.Created, create.StatusCode);
            var id = (await Body(create))["id"]!.GetValue<string>();

            foreach (var docType in new[] { "company_registration", "purchase_order" })
            {
                var document = await applicant.PostAsJsonAsync($"/api/deals/{id}/documents", new
                {
                    doc_type = docType,
                    filename = $"{docType}.pdf"
                });
                Assert.Equal(HttpStatusCode.Created, document.StatusCode);
            }

            var review = await analyst.PostAsync($"/api/deals/{id}/review", null);
            Assert.Equal(HttpStatusCode.OK, review.StatusCode);
        }

        [Fact]
        public async Task A_bearer_token_with_the_wrong_role_is_rejected_with_403()
        {
            var applicantToken = await Login("demo-applicant", "Applicant#2026");
            var analystToken = await Login("demo-analyst", "Analyst#2026");

            var deal = await CreateDeal();
            var id = deal["id"]!.GetValue<string>();

            var reviewAsApplicant = await BearerClient(applicantToken).PostAsync($"/api/deals/{id}/review", null);
            Assert.Equal(HttpStatusCode.Forbidden, reviewAsApplicant.StatusCode);

            var createAsAnalyst = await BearerClient(analystToken).PostAsJsonAsync("/api/deals", new { });
            Assert.Equal(HttpStatusCode.Forbidden, createAsAnalyst.StatusCode);
        }

        [Fact]
        public async Task Login_with_wrong_or_unknown_credentials_returns_401()
        {
            var wrongPassword = await Anonymous.PostAsJsonAsync("/api/auth/login", new
            {
                username = "demo-analyst",
                password = "not-the-password"
            });
            Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);

            var unknownUser = await Anonymous.PostAsJsonAsync("/api/auth/login", new
            {
                username = "nobody",
                password = "whatever-2026"
            });
            Assert.Equal(HttpStatusCode.Unauthorized, unknownUser.StatusCode);
        }

        [Fact]
        public async Task An_invalid_bearer_token_returns_401_and_never_falls_back_to_the_header()
        {
            var deal = await CreateDeal();
            var id = deal["id"]!.GetValue<string>();

            using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/deals/{id}/review");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-real-token");
            request.Headers.Add("X-Actor-Role", "analyst");

            var response = await Anonymous.SendAsync(request);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal("SUBMITTED", (await GetDeal(id))["status"]!.GetValue<string>());
        }

        [Fact]
        public async Task Register_validates_username_password_and_role()
        {
            var invalid = await Anonymous.PostAsJsonAsync("/api/auth/register", new
            {
                username = "ab",
                password = "short",
                role = "manager"
            });

            Assert.Equal(HttpStatusCode.UnprocessableEntity, invalid.StatusCode);
            Assert.Equal(3, (await Body(invalid))["error"]!["errors"]!.AsArray().Count);

            var duplicate = await Anonymous.PostAsJsonAsync("/api/auth/register", new
            {
                username = "Demo-Analyst",
                password = "Analyst#2026",
                role = "analyst"
            });
            Assert.Equal(HttpStatusCode.UnprocessableEntity, duplicate.StatusCode);
        }

        [Fact]
        public async Task Registered_users_can_login_and_use_their_role()
        {
            var register = await Anonymous.PostAsJsonAsync("/api/auth/register", new
            {
                username = "jane.analyst",
                password = "Sup3r-Secret!",
                role = "analyst"
            });
            Assert.Equal(HttpStatusCode.Created, register.StatusCode);
            Assert.Equal("analyst", (await Body(register))["role"]!.GetValue<string>());

            var deal = await CreateDeal();
            var id = deal["id"]!.GetValue<string>();

            var jane = BearerClient(await Login("jane.analyst", "Sup3r-Secret!"));
            var review = await jane.PostAsync($"/api/deals/{id}/review", null);
            Assert.Equal(HttpStatusCode.OK, review.StatusCode);
        }

        [Fact]
        public async Task Disabling_the_header_fallback_requires_a_bearer_token_on_every_write()
        {
            using var strictFactory = new DealDeskApiFactory();
            using var strict = strictFactory
                .WithWebHostBuilder(builder => builder.UseSetting("Auth:AllowActorRoleHeader", "false"))
                .CreateClient();

            strict.DefaultRequestHeaders.Add("X-Actor-Role", "applicant");
            var headerOnly = await strict.PostAsJsonAsync("/api/deals", new { });
            Assert.Equal(HttpStatusCode.Unauthorized, headerOnly.StatusCode);
        }
    }
}
