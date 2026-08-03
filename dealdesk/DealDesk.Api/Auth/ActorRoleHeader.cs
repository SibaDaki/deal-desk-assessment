namespace DealDesk.Api.Auth
{
    /// <summary>
    /// Auth is out of scope for this service: the caller's role arrives via the
    /// X-Actor-Role header ("applicant" or "analyst") and is trusted as-is.
    /// </summary>
    public static class ActorRoleHeader
    {
        public const string Name = "X-Actor-Role";
        public const string Analyst = "analyst";
        public const string Applicant = "applicant";

        public static string Resolve(HttpRequest request)
        {
            var value = request.Headers[Name].ToString();
            return string.IsNullOrWhiteSpace(value) ? "anonymous" : value.Trim().ToLowerInvariant();
        }
    }
}
