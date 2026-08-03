using DealDesk.Domain.Entities;
using DealDesk.Domain.Enums;

namespace DealDesk.Domain.Services
{
    /// <summary>
    /// Document gating rules that must be satisfied before a deal can be approved:
    ///  - always: company_registration
    ///  - purchase_order deals: purchase_order; invoice_discounting deals: invoice
    ///  - deals strictly above R1,000,000 (100_000_000 cents): financial_statements
    /// </summary>
    public static class DocumentRequirements
    {
        public const long LargeDealThresholdCents = 100_000_000;

        public static IReadOnlyList<DocumentType> RequiredFor(FundingType fundingType, long dealAmountCents)
        {
            var required = new List<DocumentType> { DocumentType.CompanyRegistration };

            required.Add(fundingType == FundingType.PurchaseOrder
                ? DocumentType.PurchaseOrder
                : DocumentType.Invoice);

            if (dealAmountCents > LargeDealThresholdCents)
                required.Add(DocumentType.FinancialStatements);

            return required;
        }

        public static IReadOnlyList<DocumentType> MissingFor(Deal deal) =>
            RequiredFor(deal.FundingType, deal.DealAmountCents)
                .Where(required => deal.Documents.All(attached => attached.DocType != required))
                .ToList();
    }
}
