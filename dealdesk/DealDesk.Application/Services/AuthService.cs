using DealDesk.Application.Abstractions;
using DealDesk.Application.DTOs.Requests;
using DealDesk.Application.DTOs.Responses;
using DealDesk.Domain.Common;
using DealDesk.Domain.Entities;
using DealDesk.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace DealDesk.Application.Services
{
    /// <summary>
    /// Registration and login. Passwords are hashed through the IPasswordHasher
    /// port before they reach the store; a successful login is exchanged for a
    /// signed Bearer token whose role claim drives the endpoint gates. Login
    /// failures are deliberately indistinguishable (unknown user vs. wrong
    /// password) so the API does not leak which usernames exist.
    /// </summary>
    public sealed class AuthService : IAuthService
    {
        public const int MinimumPasswordLength = 8;

        private readonly IUserRepository _users;
        private readonly IPasswordHasher _passwordHasher;
        private readonly ITokenGenerator _tokens;
        private readonly IClock _clock;
        private readonly ILogger<AuthService> _logger;

        public AuthService(
            IUserRepository users,
            IPasswordHasher passwordHasher,
            ITokenGenerator tokens,
            IClock clock,
            ILogger<AuthService> logger)
        {
            _users = users;
            _passwordHasher = passwordHasher;
            _tokens = tokens;
            _clock = clock;
            _logger = logger;
        }

        public async Task<Result<UserResponse>> RegisterAsync(
            RegisterUserRequest request, CancellationToken cancellationToken = default)
        {
            var errors = new List<string>();

            var username = request.Username?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(username) || username.Length < 3)
                errors.Add("username is required and must be at least 3 characters.");

            if (string.IsNullOrWhiteSpace(request.Password) ||
                request.Password.Length < MinimumPasswordLength)
                errors.Add($"password is required and must be at least {MinimumPasswordLength} characters.");

            if (!Wire.TryParseUserRole(request.Role, out var role))
                errors.Add($"role must be one of: {string.Join(", ", Wire.UserRoleValues)}.");

            if (errors.Count > 0)
                return Result.Failure<UserResponse>(Error.Validation("Registration is invalid.", errors));

            var user = new User(
                Guid.NewGuid(), username!, _passwordHasher.Hash(request.Password!), role, _clock.UtcNow);

            if (!await _users.AddAsync(user, cancellationToken))
                return Result.Failure<UserResponse>(Error.Validation("username is already taken."));

            _logger.LogInformation("Registered {Role} user {Username}", role.ToWire(), user.Username);
            return Result.Success(new UserResponse(user.Id, user.Username, user.Role.ToWire(), user.CreatedAt));
        }

        public async Task<Result<TokenResponse>> LoginAsync(
            LoginRequest request, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
                return Result.Failure<TokenResponse>(Error.Validation("username and password are required."));

            var user = await _users.GetByUsernameAsync(
                request.Username.Trim().ToLowerInvariant(), cancellationToken);

            if (user is null || !_passwordHasher.Verify(request.Password, user.PasswordHash))
                return Result.Failure<TokenResponse>(Error.Unauthorized());

            var token = _tokens.Generate(user);
            _logger.LogInformation("Issued token for {Role} user {Username}", user.Role.ToWire(), user.Username);

            return Result.Success(new TokenResponse(
                token.AccessToken, "Bearer", token.ExpiresAt, user.Username, user.Role.ToWire()));
        }
    }
}
