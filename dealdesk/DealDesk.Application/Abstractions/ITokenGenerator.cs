using DealDesk.Domain.Entities;

namespace DealDesk.Application.Abstractions
{
    public sealed record IssuedToken(string AccessToken, DateTimeOffset ExpiresAt);

    /// <summary>Issues signed access tokens carrying the user's identity and role.</summary>
    public interface ITokenGenerator
    {
        IssuedToken Generate(User user);
    }
}
