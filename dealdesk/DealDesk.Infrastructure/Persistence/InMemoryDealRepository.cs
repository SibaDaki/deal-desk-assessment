using System.Collections.Concurrent;
using DealDesk.Application.Abstractions;
using DealDesk.Domain.Entities;
using DealDesk.Domain.Enums;

namespace DealDesk.Infrastructure.Persistence
{
    /// <summary>
    /// Thread-safe in-memory store. The assessment allows in-memory persistence;
    /// swapping in a real datastore only requires reimplementing IDealRepository
    /// (UpdateAsync is already part of the contract for that reason).
    /// </summary>
    public sealed class InMemoryDealRepository : IDealRepository
    {
        private readonly ConcurrentDictionary<Guid, Deal> _deals = new();

        public Task AddAsync(Deal deal, CancellationToken cancellationToken = default)
        {
            if (!_deals.TryAdd(deal.Id, deal))
                throw new InvalidOperationException($"A deal with id {deal.Id} already exists.");
            return Task.CompletedTask;
        }

        public Task<Deal?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            _deals.TryGetValue(id, out var deal);
            return Task.FromResult(deal);
        }

        public Task<IReadOnlyList<Deal>> ListAsync(
            DealStatus? status, FundingType? fundingType, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<Deal> result = _deals.Values
                .Where(d => status is null || d.Status == status)
                .Where(d => fundingType is null || d.FundingType == fundingType)
                .OrderBy(d => d.CreatedAt)
                .ThenBy(d => d.Reference, StringComparer.Ordinal)
                .ToList();

            return Task.FromResult(result);
        }

        public Task UpdateAsync(Deal deal, CancellationToken cancellationToken = default)
        {
            // Mutations happen on the tracked instance itself, so there is nothing
            // to write back for the in-memory store.
            return Task.CompletedTask;
        }
    }
}
