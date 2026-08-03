namespace DealDesk.Application.DTOs.Requests
{
    public sealed class ApproveDealRequest
    {
        public int? AdvanceRateBps { get; init; }
        public int? FacilityFeeRateBps { get; init; }
        public string? ExpectedSettlementDate { get; init; }
    }
}
