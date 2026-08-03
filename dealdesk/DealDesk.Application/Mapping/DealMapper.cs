using System.Globalization;
using DealDesk.Application.DTOs.Responses;
using DealDesk.Domain.Entities;
using DealDesk.Domain.Enums;

namespace DealDesk.Application.Mapping
{
    public static class DealMapper
    {
        private const string IsoDateFormat = "yyyy-MM-dd";

        public static DealResponse ToResponse(Deal deal) => new(
            deal.Id,
            deal.Reference,
            deal.ApplicantName,
            deal.FundingType.ToWire(),
            deal.DealAmountCents,
            deal.BuyerName,
            deal.BuyerSector.ToWire(),
            deal.Status.ToWire(),
            deal.Documents.Select(d => new DocumentResponse(d.DocType.ToWire(), d.Filename)).ToList(),
            deal.Terms is null
                ? null
                : new TermsResponse(
                    deal.Terms.AdvanceRateBps,
                    deal.Terms.FacilityFeeRateBps,
                    FormatDate(deal.Terms.ExpectedSettlementDate)),
            deal.FundedAmountCents,
            deal.FundedOn is null ? null : FormatDate(deal.FundedOn.Value),
            deal.TotalFeeCents,
            deal.TotalRepayableCents,
            deal.DeclineReason,
            deal.CreatedAt,
            deal.AuditTrail
                .Select(a => new AuditEntryResponse(
                    a.Action, a.FromStatus?.ToWire(), a.ToStatus.ToWire(), a.ActorRole, a.Timestamp))
                .ToList());

        public static StatementResponse ToStatement(Deal deal) => new(
            deal.Id,
            deal.Reference,
            deal.Status.ToWire(),
            deal.FundedAmountCents ?? 0,
            deal.TotalFeeCents ?? 0,
            deal.TotalRepayableCents ?? 0,
            deal.TotalRepaidCents,
            deal.FundedAmountCents.HasValue ? deal.OutstandingCents : 0);

        public static string FormatDate(DateOnly date) =>
            date.ToString(IsoDateFormat, CultureInfo.InvariantCulture);

        public static bool TryParseIsoDate(string? input, out DateOnly date) =>
            DateOnly.TryParseExact(input?.Trim(), IsoDateFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out date);
    }
}
