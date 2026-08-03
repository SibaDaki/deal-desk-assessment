namespace DealDesk.Application.DTOs.Responses
{
    /// <summary>Public view of a user; the password hash never leaves the service.</summary>
    public sealed record UserResponse(
        Guid Id,
        string Username,
        string Role,
        DateTimeOffset CreatedAt);

    /// <summary>A successful login: a Bearer token and when it stops working.</summary>
    public sealed record TokenResponse(
        string AccessToken,
        string TokenType,
        DateTimeOffset ExpiresAt,
        string Username,
        string Role);
}
