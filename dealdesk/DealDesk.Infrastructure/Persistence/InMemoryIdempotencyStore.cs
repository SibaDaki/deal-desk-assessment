using System.Collections.Concurrent;
using DealDesk.Application.Abstractions;

namespace DealDesk.Infrastructure.Persistence
{
    public sealed class InMemoryIdempotencyStore : IIdempotencyStore
    {
        private readonly ConcurrentDictionary<string, Guid> _keys = new(StringComparer.Ordinal);

        public bool TryGet(string key, out Guid dealId) => _keys.TryGetValue(key, out dealId);

        public void Put(string key, Guid dealId) => _keys.TryAdd(key, dealId);
    }
}
