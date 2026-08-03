using System.Collections.Concurrent;
using DealDesk.Application.Abstractions;
using DealDesk.Domain.Entities;

namespace DealDesk.Infrastructure.Persistence
{
    /// <summary>
    /// Thread-safe in-memory user store keyed by lowercase username, so
    /// uniqueness is enforced atomically even under concurrent registrations.
    /// </summary>
    public sealed class InMemoryUserRepository : IUserRepository
    {
        private readonly ConcurrentDictionary<string, User> _users = new();

        public Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default) =>
            Task.FromResult(_users.TryGetValue(username.ToLowerInvariant(), out var user) ? user : null);

        public Task<bool> AddAsync(User user, CancellationToken cancellationToken = default) =>
            Task.FromResult(_users.TryAdd(user.Username.ToLowerInvariant(), user));
    }
}
