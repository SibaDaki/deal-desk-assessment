using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using DealDesk.Application.Abstractions;
using DealDesk.Domain.Entities;
using DealDesk.Domain.Enums;
using Microsoft.IdentityModel.Tokens;

namespace DealDesk.Infrastructure.Auth
{
    /// <summary>
    /// Issues HMAC-SHA256-signed JWTs. The "role" claim carries the wire-format
    /// role ("applicant"/"analyst"), which is what the endpoint gates check.
    /// </summary>
    public sealed class JwtTokenGenerator : ITokenGenerator
    {
        private readonly JwtOptions _options;
        private readonly IClock _clock;

        public JwtTokenGenerator(JwtOptions options, IClock clock)
        {
            _options = options;
            _clock = clock;
        }

        public IssuedToken Generate(User user)
        {
            var now = _clock.UtcNow;
            var expiresAt = now.AddMinutes(_options.TokenLifetimeMinutes);

            var credentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)),
                SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _options.Issuer,
                audience: _options.Audience,
                claims: new[]
                {
                    new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                    new Claim(JwtRegisteredClaimNames.UniqueName, user.Username),
                    new Claim("role", user.Role.ToWire()),
                    new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
                },
                notBefore: now.UtcDateTime,
                expires: expiresAt.UtcDateTime,
                signingCredentials: credentials);

            return new IssuedToken(new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
        }
    }
}
