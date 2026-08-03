using DealDesk.Domain.Enums;
using DealDesk.Domain.Services;
using Xunit;

namespace DealDesk.Tests.Unit
{
    public class DocumentRequirementsTests
    {
        [Fact]
        public void Purchase_order_deal_requires_company_registration_and_purchase_order()
        {
            var required = DocumentRequirements.RequiredFor(FundingType.PurchaseOrder, 50_000_000);

            Assert.Equal(
                new[] { DocumentType.CompanyRegistration, DocumentType.PurchaseOrder },
                required);
        }

        [Fact]
        public void Invoice_discounting_deal_requires_invoice_instead_of_purchase_order()
        {
            var required = DocumentRequirements.RequiredFor(FundingType.InvoiceDiscounting, 50_000_000);

            Assert.Equal(
                new[] { DocumentType.CompanyRegistration, DocumentType.Invoice },
                required);
        }

        [Fact]
        public void Exactly_one_million_rand_does_not_trigger_financial_statements()
        {
            var required = DocumentRequirements.RequiredFor(FundingType.PurchaseOrder, 100_000_000);

            Assert.DoesNotContain(DocumentType.FinancialStatements, required);
        }

        [Fact]
        public void Above_one_million_rand_requires_financial_statements()
        {
            var required = DocumentRequirements.RequiredFor(FundingType.PurchaseOrder, 100_000_001);

            Assert.Contains(DocumentType.FinancialStatements, required);
        }
    }
}
