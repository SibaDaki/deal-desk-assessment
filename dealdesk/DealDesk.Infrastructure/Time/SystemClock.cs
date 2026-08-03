using DealDesk.Application.Abstractions;

namespace DealDesk.Infrastructure.Time
{
    public sealed class SystemClock : IClock
    {
        public DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }
}
