namespace DealDesk.Domain.ValueObjects
{
    /// <summary>Underwriting terms set when a deal is approved.</summary>
    public sealed record DealTerms(
        int AdvanceRateBps,
        int FacilityFeeRateBps,
        DateOnly ExpectedSettlementDate);
}
