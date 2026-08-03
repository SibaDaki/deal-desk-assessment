namespace DealDesk.Domain.Services
{
    /// <summary>
    /// Deterministic money maths. All amounts are integer ZAR cents and all rates
    /// are integer basis points (10000 bps = 100%). Rounding rule: round half up,
    /// implemented purely in integer arithmetic so there is no floating-point drift.
    /// </summary>
    public static class MoneyMath
    {
        public const int BpsScale = 10_000;

        /// <summary>
        /// Applies a basis-point rate to an amount in cents, rounding half up.
        /// e.g. ApplyBps(50_000_000, 9000) = 45_000_000.
        /// </summary>
        public static long ApplyBps(long amountCents, int rateBps)
        {
            if (amountCents < 0)
                throw new ArgumentOutOfRangeException(nameof(amountCents), "Amount cannot be negative.");
            if (rateBps < 0)
                throw new ArgumentOutOfRangeException(nameof(rateBps), "Rate cannot be negative.");

            return (amountCents * rateBps + BpsScale / 2) / BpsScale;
        }
    }
}
