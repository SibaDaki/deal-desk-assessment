using DealDesk.Domain.Entities;

namespace DealDesk.Application.Abstractions
{
    public interface IUserRepository
    {
        Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default);

        /// <summary>Adds the user; returns false when the username is already taken.</summary>
        Task<bool> AddAsync(User user, CancellationToken cancellationToken = default);
    }
}
