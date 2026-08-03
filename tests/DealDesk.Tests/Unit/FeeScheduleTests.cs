using DealDesk.Domain.Services;
using Xunit;

namespace DealDesk.Tests.Unit
{
    public class FeeScheduleTests
    {
        [Theory]
        [InlineData("2026-07-01", "2026-08-30", 2)] // 60 days -> 2 periods (scenario 10)
        [InlineData("2026-07-01", "2026-07-31", 1)] // 30 days -> 1 period
        [InlineData("2026-07-01", "2026-08-01", 2)] // 31 days -> a second period has started
        [InlineData("2026-07-01", "2026-07-02", 1)] // 1 day -> minimum one period
        [InlineData("2026-07-01", "2026-09-29", 3)] // 90 days -> 3 periods
        [InlineData("2026-07-01", "2026-08-31", 3)] // 61 days -> 3 periods
        [InlineData("2026-07-01", "2026-07-01", 1)] // same day -> minimum one period
        public void Counts_started_30_day_periods(string fundedOn, string settlement, int expected)
        {
            Assert.Equal(expected,
                FeeSchedule.CountPeriods(DateOnly.Parse(fundedOn), DateOnly.Parse(settlement)));
        }

        [Fact]
        public void Scenario_10_fee_maths_are_exact()
        {
            // funded 45_000_000 at 350 bps over 60 days (2 periods):
            // per-period fee = 1_575_000, total = 3_150_000
            var totalFee = FeeSchedule.TotalFeeCents(
                45_000_000, 350, new DateOnly(2026, 7, 1), new DateOnly(2026, 8, 30));

            Assert.Equal(3_150_000, totalFee);
        }

        [Fact]
        public void Rounding_happens_per_period_before_multiplying()
        {
            // 3333 cents at 350 bps = 116.655 -> 117 per period (half up);
            // 2026-01-01 -> 2026-03-31 = 89 days = 3 started periods; 117 x 3 = 351.
            // Rounding then multiplying is the documented rule (vs round(116.655 x 3) = 350).
            var totalFee = FeeSchedule.TotalFeeCents(
                3333, 350, new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31));

            Assert.Equal(351, totalFee);
        }
    }
}
