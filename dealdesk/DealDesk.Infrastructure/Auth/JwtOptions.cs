namespace DealDesk.Infrastructure.Auth
{
    /// <summary>
    /// JWT settings bound from the "Jwt" configuration section. The signing key
    /// in appsettings.json is for local development only; production deployments
    /// must override it (e.g. Jwt__SigningKey environment variable).
    /// </summary>
    public sealed class JwtOptions
    {
        public const string SectionName = "Jwt";
        public const int MinimumSigningKeyLength = 32;

        public string Issuer { get; init; } = "dealdesk-api";
        public string Audience { get; init; } = "dealdesk-clients";
        public string SigningKey { get; init; } = string.Empty;
        public int TokenLifetimeMinutes { get; init; } = 60;
    }
}
