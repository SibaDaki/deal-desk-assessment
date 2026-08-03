namespace DealDesk.Application.DTOs.Responses
{
    public sealed record DocumentResponse(string DocType, string Filename);

    public sealed record TermsResponse(
        int AdvanceRateBps,
        int FacilityFeeRateBps,
        string ExpectedSettlementDate);

    public sealed record AuditEntryResponse(
        string Action,
        string? FromStatus,
        string ToStatus,
        string ActorRole,
        DateTimeOffset Timestamp);

    public sealed record DealResponse(
        Guid Id,
        string Reference,
        string ApplicantName,
        string FundingType,
        long DealAmountCents,
        string BuyerName,
        string BuyerSector,
        string Status,
        IReadOnlyList<DocumentResponse> Documents,
        TermsResponse? Terms,
        long? FundedAmountCents,
        string? FundedOn,
        long? TotalFeeCents,
        long? TotalRepayableCents,
        string? DeclineReason,
        DateTimeOffset CreatedAt,
        IReadOnlyList<AuditEntryResponse> AuditTrail);

    public sealed record CreateDealOutcome(DealResponse Deal, bool Replayed);

    public sealed record PagedDeals(
        IReadOnlyList<DealResponse> Items,
        int TotalCount,
        int? Page,
        int? PageSize);

    public sealed record RepaymentResponse(
        Guid Id,
        Guid DealId,
        long AmountCents,
        DateTimeOffset RecordedAt,
        string DealStatus,
        long OutstandingCents);

    public sealed record StatementResponse(
        Guid DealId,
        string Reference,
        string Status,
        long FundedAmountCents,
        long TotalFeeCents,
        long TotalRepayableCents,
        long TotalRepaidCents,
        long OutstandingCents);
}
