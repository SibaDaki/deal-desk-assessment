using DealDesk.Domain.Entities;
using DealDesk.Domain.Enums;

namespace DealDesk.Application.Abstractions
{
    public interface IDealRepository
    {
        Task AddAsync(Deal deal, CancellationToken cancellationToken = default);
        Task<Deal?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<Deal>> ListAsync(
            DealStatus? status,
            FundingType? fundingType,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Persists changes to an existing deal. A no-op for the in-memory store,
        /// but kept on the interface so a real datastore can be swapped in.
        /// </summary>
        Task UpdateAsync(Deal deal, CancellationToken cancellationToken = default);
    }
}
