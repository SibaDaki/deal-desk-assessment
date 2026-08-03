namespace DealDesk.Application.Abstractions
{
    /// <summary>
    /// Maps Idempotency-Key header values to the deal they created, so replaying
    /// the same POST /api/deals does not create a duplicate deal.
    /// </summary>
    public interface IIdempotencyStore
    {
        bool TryGet(string key, out Guid dealId);
        void Put(string key, Guid dealId);
    }
}
