using System.Security.Claims;

namespace DealDesk.Api.Auth
{
    /// <summary>
    /// Resolves the acting role for audit purposes. An authenticated caller's
    /// role comes from the JWT role claim; otherwise the X-Actor-Role header
    /// ("applicant" or "analyst") is used, matching the assessment contract.
    /// </summary>
    public static class ActorRoleHeader
    {
        public const string Name = "X-Actor-Role";
        public const string Analyst = "analyst";
        public const string Applicant = "applicant";

        public static string Resolve(HttpRequest request)
        {
            var user = request.HttpContext.User;
            if (user.Identity?.IsAuthenticated == true)
            {
                var claimedRole = user.FindFirstValue(ClaimTypes.Role) ?? user.FindFirstValue("role");
                if (!string.IsNullOrWhiteSpace(claimedRole))
                    return claimedRole.Trim().ToLowerInvariant();
            }

            var value = request.Headers[Name].ToString();
            return string.IsNullOrWhiteSpace(value) ? "anonymous" : value.Trim().ToLowerInvariant();
        }
    }
}
