using DealDesk.Application.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace DealDesk.Tests.Integration
{
    public sealed class FixedClock : IClock
    {
        public FixedClock(DateOnly today)
        {
            Today = today;
            UtcNow = new DateTimeOffset(today.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        }

        public DateOnly Today { get; }
        public DateTimeOffset UtcNow { get; }
    }

    /// <summary>
    /// Boots the real API in-process with a fixed clock (2026-06-01) so the
    /// date-sensitive rules in the acceptance scenarios are deterministic.
    /// </summary>
    public sealed class DealDeskApiFactory : WebApplicationFactory<Program>
    {
        public static readonly DateOnly Today = new(2026, 6, 1);

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                var clock = services.Single(d => d.ServiceType == typeof(IClock));
                services.Remove(clock);
                services.AddSingleton<IClock>(new FixedClock(Today));
            });
        }
    }
}
