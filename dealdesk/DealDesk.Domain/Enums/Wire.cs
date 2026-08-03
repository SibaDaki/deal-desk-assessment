namespace DealDesk.Domain.Enums
{
    /// <summary>
    /// Canonical wire-format strings for the domain enums, exactly as they appear
    /// in the API contract (statuses in UPPER_SNAKE, everything else in lower_snake).
    /// Parsing is case-insensitive; formatting is always canonical.
    /// </summary>
    public static class Wire
    {
        private static readonly Dictionary<DealStatus, string> StatusToWire = new()
        {
            [DealStatus.Submitted] = "SUBMITTED",
            [DealStatus.UnderReview] = "UNDER_REVIEW",
            [DealStatus.Approved] = "APPROVED",
            [DealStatus.Funded] = "FUNDED",
            [DealStatus.Settled] = "SETTLED",
            [DealStatus.Declined] = "DECLINED"
        };

        private static readonly Dictionary<FundingType, string> FundingTypeToWire = new()
        {
            [FundingType.PurchaseOrder] = "purchase_order",
            [FundingType.InvoiceDiscounting] = "invoice_discounting"
        };

        private static readonly Dictionary<BuyerSector, string> BuyerSectorToWire = new()
        {
            [BuyerSector.Government] = "government",
            [BuyerSector.Soe] = "soe",
            [BuyerSector.Corporate] = "corporate",
            [BuyerSector.Private] = "private"
        };

        private static readonly Dictionary<DocumentType, string> DocumentTypeToWire = new()
        {
            [DocumentType.PurchaseOrder] = "purchase_order",
            [DocumentType.Invoice] = "invoice",
            [DocumentType.CompanyRegistration] = "company_registration",
            [DocumentType.FinancialStatements] = "financial_statements",
            [DocumentType.TaxClearance] = "tax_clearance",
            [DocumentType.BankStatement] = "bank_statement",
            [DocumentType.IdDocument] = "id_document"
        };

        public static string ToWire(this DealStatus value) => StatusToWire[value];
        public static string ToWire(this FundingType value) => FundingTypeToWire[value];
        public static string ToWire(this BuyerSector value) => BuyerSectorToWire[value];
        public static string ToWire(this DocumentType value) => DocumentTypeToWire[value];

        public static bool TryParseStatus(string? input, out DealStatus value) =>
            TryParse(StatusToWire, input, out value);

        public static bool TryParseFundingType(string? input, out FundingType value) =>
            TryParse(FundingTypeToWire, input, out value);

        public static bool TryParseBuyerSector(string? input, out BuyerSector value) =>
            TryParse(BuyerSectorToWire, input, out value);

        public static bool TryParseDocumentType(string? input, out DocumentType value) =>
            TryParse(DocumentTypeToWire, input, out value);

        public static IReadOnlyCollection<string> FundingTypeValues => FundingTypeToWire.Values;
        public static IReadOnlyCollection<string> BuyerSectorValues => BuyerSectorToWire.Values;
        public static IReadOnlyCollection<string> DocumentTypeValues => DocumentTypeToWire.Values;

        private static bool TryParse<TEnum>(Dictionary<TEnum, string> map, string? input, out TEnum value)
            where TEnum : struct
        {
            foreach (var pair in map)
            {
                if (string.Equals(pair.Value, input?.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    value = pair.Key;
                    return true;
                }
            }

            value = default;
            return false;
        }
    }
}
