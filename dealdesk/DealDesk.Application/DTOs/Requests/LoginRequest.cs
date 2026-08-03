namespace DealDesk.Application.DTOs.Requests
{
    /// <summary>
    /// All properties are nullable on purpose: missing fields must surface as a 422
    /// from our own validation, not as a framework model-binding 400.
    /// </summary>
    public sealed class LoginRequest
    {
        public string? Username { get; init; }
        public string? Password { get; init; }
    }
}
