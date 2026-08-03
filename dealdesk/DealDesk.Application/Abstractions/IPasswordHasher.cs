namespace DealDesk.Application.Abstractions
{
    /// <summary>One-way password hashing; the implementation owns the algorithm and format.</summary>
    public interface IPasswordHasher
    {
        string Hash(string password);
        bool Verify(string password, string passwordHash);
    }
}
