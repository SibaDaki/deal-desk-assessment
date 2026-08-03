namespace DealDesk.Domain.Services
{
    /// <summary>
    /// The facility fee model: a fixed per-period fee that accrues per *started*
    /// 30-day period between funding and expected settlement, minimum one period.
    /// The fee is computed once at funding time and never changes afterwards.
    /// </summary>
    public static class FeeSchedule
    {
        public const int DaysPerPeriod = 30;

        /// <summary>
        /// periods = max(1, ceil(days_between(funded_on, expected_settlement_date) / 30))
        /// </summary>
        public static int CountPeriods(DateOnly fundedOn, DateOnly expectedSettlementDate)
        {
            var days = expectedSettlementDate.DayNumber - fundedOn.DayNumber;
            if (days <= 0)
                return 1;

            return (days + DaysPerPeriod - 1) / DaysPerPeriod;
        }

        /// <summary>
        /// total_fee_cents = round_half_up(funded_amount_cents * facility_fee_rate_bps / 10000) * periods
        /// </summary>
        public static long TotalFeeCents(
            long fundedAmountCents,
            int facilityFeeRateBps,
            DateOnly fundedOn,
            DateOnly expectedSettlementDate)
        {
            var feePerPeriod = MoneyMath.ApplyBps(fundedAmountCents, facilityFeeRateBps);
            return feePerPeriod * CountPeriods(fundedOn, expectedSettlementDate);
        }
    }
}
