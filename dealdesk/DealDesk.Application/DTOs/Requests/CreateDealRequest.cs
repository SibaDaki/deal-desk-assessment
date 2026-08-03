namespace DealDesk.Application.DTOs.Requests
{
    /// <summary>
    /// All properties are nullable on purpose: missing fields must surface as a 422
    /// from our own validation, not as a framework model-binding 400.
    /// </summary>
    public sealed class CreateDealRequest
    {
        public string? ApplicantName { get; init; }
        public string? FundingType { get; init; }
        public long? DealAmountCents { get; init; }
        public string? BuyerName { get; init; }
        public string? BuyerSector { get; init; }
    }
}
