using DealDesk.Domain.Enums;

namespace DealDesk.Domain.Entities
{
    /// <summary>
    /// An authenticated caller of the API. Passwords are never stored: only a
    /// one-way hash is kept, and the role decides which endpoints the user may
    /// call (applicants create deals and upload documents, analysts underwrite).
    /// </summary>
    public sealed class User
    {
        public User(Guid id, string username, string passwordHash, UserRole role, DateTimeOffset createdAt)
        {
            Id = id;
            Username = username;
            PasswordHash = passwordHash;
            Role = role;
            CreatedAt = createdAt;
        }

        public Guid Id { get; }
        public string Username { get; }
        public string PasswordHash { get; }
        public UserRole Role { get; }
        public DateTimeOffset CreatedAt { get; }
    }
}
