using DealDesk.Domain.Services;
using Xunit;

namespace DealDesk.Tests.Unit
{
    public class MoneyMathTests
    {
        [Theory]
        [InlineData(50_000_000, 9000, 45_000_000)] // scenario 9: 90% advance on R500k
        [InlineData(45_000_000, 350, 1_575_000)]   // scenario 10: 3.5% fee per period
        [InlineData(25_000_000, 10_000, 25_000_000)] // 100% advance
        [InlineData(25_000_000, 0, 0)]
        public void Applies_bps_exactly(long amount, int bps, long expected)
        {
            Assert.Equal(expected, MoneyMath.ApplyBps(amount, bps));
        }

        [Theory]
        [InlineData(1, 5000, 1)]  // 0.5 rounds up to 1
        [InlineData(3, 5000, 2)]  // 1.5 rounds up to 2
        [InlineData(5, 1000, 1)]  // 0.5 rounds up to 1
        [InlineData(4, 1000, 0)]  // 0.4 rounds down to 0
        [InlineData(333, 350, 12)] // 11.655 rounds to 12
        public void Rounds_half_up(long amount, int bps, long expected)
        {
            Assert.Equal(expected, MoneyMath.ApplyBps(amount, bps));
        }

        [Fact]
        public void Rejects_negative_inputs()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => MoneyMath.ApplyBps(-1, 100));
            Assert.Throws<ArgumentOutOfRangeException>(() => MoneyMath.ApplyBps(100, -1));
        }
    }
}
