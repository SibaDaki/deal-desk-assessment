namespace DealDesk.Application.Abstractions
{
    /// <summary>Generates unique human references in the format SF-{year}-{sequential}, e.g. SF-2026-0001.</summary>
    public interface IReferenceGenerator
    {
        string NextReference(int year);

        /// <summary>
        /// Marks sequence numbers up to and including <paramref name="sequence"/> as
        /// consumed for the given year, so references loaded from seed data or an
        /// existing datastore are never reissued.
        /// </summary>
        void EnsureUsed(int year, int sequence);
    }
}
