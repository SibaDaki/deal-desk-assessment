using DealDesk.Domain.Common;
using DealDesk.Domain.Entities;
using DealDesk.Domain.Enums;
using Xunit;

namespace DealDesk.Tests.Unit
{
    public class DealAggregateTests
    {
        private static readonly DateOnly Today = new(2026, 6, 1);
        private static readonly DateTimeOffset Now = new(2026, 6, 1, 8, 0, 0, TimeSpan.Zero);
        private const string Analyst = "analyst";

        private static Deal NewDeal(long amountCents = 50_000_000) =>
            Deal.Create("SF-2026-0001", "Thandi Trading", FundingType.PurchaseOrder,
                amountCents, "Eskom", BuyerSector.Soe, "applicant", Now);

        private static Deal UnderwrittenDeal(long amountCents = 50_000_000)
        {
            var deal = NewDeal(amountCents);
            deal.AttachDocument(DocumentType.CompanyRegistration, "cipc.pdf");
            deal.AttachDocument(DocumentType.PurchaseOrder, "po.pdf");
            Assert.True(deal.StartReview(Analyst, Now).IsSuccess);
            Assert.True(deal.Approve(9000, 350, new DateOnly(2026, 8, 30), Today, Analyst, Now).IsSuccess);
            return deal;
        }

        [Fact]
        public void Review_moves_submitted_deal_under_review()
        {
            var deal = NewDeal();

            var result = deal.StartReview(Analyst, Now);

            Assert.True(result.IsSuccess);
            Assert.Equal(DealStatus.UnderReview, deal.Status);
        }

        [Theory]
        [InlineData("approve")] // approve straight from SUBMITTED
        [InlineData("fund")]    // fund straight from SUBMITTED
        public void Illegal_transitions_from_submitted_are_conflicts(string action)
        {
            var deal = NewDeal();

            var result = action switch
            {
                "approve" => deal.Approve(9000, 350, new DateOnly(2026, 8, 30), Today, Analyst, Now),
                _ => deal.Fund(new DateOnly(2026, 7, 1), Analyst, Now)
            };

            Assert.True(result.IsFailure);
            Assert.Equal(ErrorType.Conflict, result.Error!.Type);
            Assert.Equal(DealStatus.Submitted, deal.Status);
        }

        [Fact]
        public void Approve_without_required_documents_lists_the_missing_types_and_does_not_transition()
        {
            var deal = NewDeal();
            deal.AttachDocument(DocumentType.CompanyRegistration, "cipc.pdf");
            deal.StartReview(Analyst, Now);

            var result = deal.Approve(9000, 350, new DateOnly(2026, 8, 30), Today, Analyst, Now);

            Assert.True(result.IsFailure);
            Assert.Equal(ErrorType.Validation, result.Error!.Type);
            Assert.Equal("missing_documents", result.Error.Code);
            Assert.Equal(new[] { "purchase_order" },
                (IReadOnlyList<string>)result.Error.Details!["missing_documents"]!);
            Assert.Equal(DealStatus.UnderReview, deal.Status);
        }

        [Fact]
        public void Large_deal_additionally_requires_financial_statements()
        {
            var deal = NewDeal(150_000_000);
            deal.AttachDocument(DocumentType.CompanyRegistration, "cipc.pdf");
            deal.AttachDocument(DocumentType.PurchaseOrder, "po.pdf");
            deal.StartReview(Analyst, Now);

            var result = deal.Approve(9000, 350, new DateOnly(2026, 8, 30), Today, Analyst, Now);

            Assert.True(result.IsFailure);
            Assert.Equal(new[] { "financial_statements" },
                (IReadOnlyList<string>)result.Error!.Details!["missing_documents"]!);
        }

        [Theory]
        [InlineData(0, 350)]      // advance below range
        [InlineData(10001, 350)]  // advance above range
        [InlineData(9000, -1)]    // negative fee rate
        public void Approve_rejects_invalid_terms(int advanceBps, int feeBps)
        {
            var deal = NewDeal();
            deal.AttachDocument(DocumentType.CompanyRegistration, "cipc.pdf");
            deal.AttachDocument(DocumentType.PurchaseOrder, "po.pdf");
            deal.StartReview(Analyst, Now);

            var result = deal.Approve(advanceBps, feeBps, new DateOnly(2026, 8, 30), Today, Analyst, Now);

            Assert.True(result.IsFailure);
            Assert.Equal(ErrorType.Validation, result.Error!.Type);
            Assert.Equal(DealStatus.UnderReview, deal.Status);
        }

        [Fact]
        public void Approve_rejects_settlement_date_not_after_today()
        {
            var deal = NewDeal();
            deal.AttachDocument(DocumentType.CompanyRegistration, "cipc.pdf");
            deal.AttachDocument(DocumentType.PurchaseOrder, "po.pdf");
            deal.StartReview(Analyst, Now);

            var result = deal.Approve(9000, 350, Today, Today, Analyst, Now);

            Assert.True(result.IsFailure);
            Assert.Equal(ErrorType.Validation, result.Error!.Type);
        }

        [Fact]
        public void Decline_is_allowed_from_submitted_and_under_review_but_not_after_approval()
        {
            var fromSubmitted = NewDeal();
            Assert.True(fromSubmitted.Decline("Buyer unverified", Analyst, Now).IsSuccess);
            Assert.Equal(DealStatus.Declined, fromSubmitted.Status);
            Assert.Equal("Buyer unverified", fromSubmitted.DeclineReason);

            var fromReview = NewDeal();
            fromReview.StartReview(Analyst, Now);
            Assert.True(fromReview.Decline("Weak financials", Analyst, Now).IsSuccess);

            var approved = UnderwrittenDeal();
            var result = approved.Decline("Too late", Analyst, Now);
            Assert.True(result.IsFailure);
            Assert.Equal(ErrorType.Conflict, result.Error!.Type);
        }

        [Fact]
        public void Terminal_states_permit_no_further_transitions()
        {
            var declined = NewDeal();
            declined.Decline("No docs", Analyst, Now);

            Assert.Equal(ErrorType.Conflict, declined.StartReview(Analyst, Now).Error!.Type);
            Assert.Equal(ErrorType.Conflict,
                declined.Approve(9000, 350, new DateOnly(2026, 8, 30), Today, Analyst, Now).Error!.Type);
            Assert.Equal(ErrorType.Conflict, declined.Fund(new DateOnly(2026, 7, 1), Analyst, Now).Error!.Type);
            Assert.Equal(ErrorType.Conflict, declined.Decline("again", Analyst, Now).Error!.Type);
        }

        [Fact]
        public void Funding_computes_advance_and_fixes_the_fee()
        {
            var deal = UnderwrittenDeal();

            var result = deal.Fund(new DateOnly(2026, 7, 1), Analyst, Now);

            Assert.True(result.IsSuccess);
            Assert.Equal(DealStatus.Funded, deal.Status);
            Assert.Equal(45_000_000, deal.FundedAmountCents);
            Assert.Equal(3_150_000, deal.TotalFeeCents);       // 1_575_000 x 2 periods
            Assert.Equal(48_150_000, deal.TotalRepayableCents);
        }

        [Fact]
        public void Repayments_settle_the_deal_exactly_at_total_repayable()
        {
            var deal = UnderwrittenDeal();
            deal.Fund(new DateOnly(2026, 7, 1), Analyst, Now);

            Assert.True(deal.RecordRepayment(40_000_000, Analyst, Now).IsSuccess);
            Assert.Equal(DealStatus.Funded, deal.Status);
            Assert.Equal(8_150_000, deal.OutstandingCents);

            Assert.True(deal.RecordRepayment(8_150_000, Analyst, Now).IsSuccess);
            Assert.Equal(DealStatus.Settled, deal.Status);
            Assert.Equal(0, deal.OutstandingCents);
        }

        [Fact]
        public void Overpayment_is_rejected_and_balance_unchanged()
        {
            var deal = UnderwrittenDeal();
            deal.Fund(new DateOnly(2026, 7, 1), Analyst, Now);

            var result = deal.RecordRepayment(48_150_001, Analyst, Now);

            Assert.True(result.IsFailure);
            Assert.Equal(ErrorType.Validation, result.Error!.Type);
            Assert.Equal(48_150_000L, result.Error.Details!["outstanding_cents"]);
            Assert.Equal(48_150_000, deal.OutstandingCents);
            Assert.Equal(DealStatus.Funded, deal.Status);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-500)]
        public void Non_positive_repayments_are_rejected(long amount)
        {
            var deal = UnderwrittenDeal();
            deal.Fund(new DateOnly(2026, 7, 1), Analyst, Now);

            var result = deal.RecordRepayment(amount, Analyst, Now);

            Assert.True(result.IsFailure);
            Assert.Equal(ErrorType.Validation, result.Error!.Type);
        }

        [Fact]
        public void Repayments_against_settled_or_unfunded_deals_are_validation_failures()
        {
            var unfunded = UnderwrittenDeal();
            Assert.Equal(ErrorType.Validation, unfunded.RecordRepayment(1000, Analyst, Now).Error!.Type);

            var settled = UnderwrittenDeal();
            settled.Fund(new DateOnly(2026, 7, 1), Analyst, Now);
            settled.RecordRepayment(48_150_000, Analyst, Now);
            Assert.Equal(ErrorType.Validation, settled.RecordRepayment(1, Analyst, Now).Error!.Type);
        }

        [Fact]
        public void Audit_trail_records_every_transition_with_actor()
        {
            var deal = UnderwrittenDeal();
            deal.Fund(new DateOnly(2026, 7, 1), Analyst, Now);
            deal.RecordRepayment(48_150_000, Analyst, Now);

            Assert.Equal(
                new[] { "create", "review", "approve", "fund", "settle" },
                deal.AuditTrail.Select(a => a.Action).ToArray());
            Assert.All(deal.AuditTrail.Skip(1), entry => Assert.Equal(Analyst, entry.ActorRole));
        }
    }
}
