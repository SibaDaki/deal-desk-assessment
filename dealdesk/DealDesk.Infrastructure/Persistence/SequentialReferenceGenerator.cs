using DealDesk.Application.Abstractions;

namespace DealDesk.Infrastructure.Persistence
{
    /// <summary>
    /// Generates SF-{year}-{sequential} references with a per-year counter,
    /// e.g. SF-2026-0001. Locked so concurrent intakes never share a number.
    /// </summary>
    public sealed class SequentialReferenceGenerator : IReferenceGenerator
    {
        private readonly object _gate = new();
        private readonly Dictionary<int, int> _countersByYear = new();

        public string NextReference(int year)
        {
            int next;
            lock (_gate)
            {
                _countersByYear.TryGetValue(year, out var current);
                next = current + 1;
                _countersByYear[year] = next;
            }

            return $"SF-{year}-{next:D4}";
        }

        public void EnsureUsed(int year, int sequence)
        {
            lock (_gate)
            {
                _countersByYear.TryGetValue(year, out var current);
                if (sequence > current)
                    _countersByYear[year] = sequence;
            }
        }
    }
}
