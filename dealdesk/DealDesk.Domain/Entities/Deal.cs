using DealDesk.Domain.Common;
using DealDesk.Domain.Enums;
using DealDesk.Domain.Services;
using DealDesk.Domain.ValueObjects;

namespace DealDesk.Domain.Entities
{
    /// <summary>
    /// Aggregate root for a funding application. Every state change goes through a
    /// method on this class, so the state machine, document gating and money rules
    /// live in one place and can be unit-tested without any transport concerns.
    /// </summary>
    public sealed class Deal
    {
        /// <summary>R250,000 intake minimum.</summary>
        public const long MinimumDealAmountCents = 25_000_000;

        private readonly List<DealDocument> _documents = new();
        private readonly List<Repayment> _repayments = new();
        private readonly List<AuditEntry> _auditTrail = new();

        private Deal(
            Guid id,
            string reference,
            string applicantName,
            FundingType fundingType,
            long dealAmountCents,
            string buyerName,
            BuyerSector buyerSector,
            DateTimeOffset createdAt)
        {
            Id = id;
            Reference = reference;
            ApplicantName = applicantName;
            FundingType = fundingType;
            DealAmountCents = dealAmountCents;
            BuyerName = buyerName;
            BuyerSector = buyerSector;
            Status = DealStatus.Submitted;
            CreatedAt = createdAt;
        }

        public Guid Id { get; }
        public string Reference { get; }
        public string ApplicantName { get; }
        public FundingType FundingType { get; }
        public long DealAmountCents { get; }
        public string BuyerName { get; }
        public BuyerSector BuyerSector { get; }
        public DealStatus Status { get; private set; }
        public DealTerms? Terms { get; private set; }
        public long? FundedAmountCents { get; private set; }
        public DateOnly? FundedOn { get; private set; }
        public long? TotalFeeCents { get; private set; }
        public long? TotalRepayableCents { get; private set; }
        public string? DeclineReason { get; private set; }
        public DateTimeOffset CreatedAt { get; }

        public IReadOnlyList<DealDocument> Documents => _documents;
        public IReadOnlyList<Repayment> Repayments => _repayments;
        public IReadOnlyList<AuditEntry> AuditTrail => _auditTrail;

        public long TotalRepaidCents => _repayments.Sum(r => r.AmountCents);
        public long OutstandingCents => (TotalRepayableCents ?? 0) - TotalRepaidCents;

        public static Deal Create(
            string reference,
            string applicantName,
            FundingType fundingType,
            long dealAmountCents,
            string buyerName,
            BuyerSector buyerSector,
            string actorRole,
            DateTimeOffset timestamp)
        {
            if (dealAmountCents < MinimumDealAmountCents)
                throw new ArgumentOutOfRangeException(nameof(dealAmountCents),
                    $"Deal amount must be at least {MinimumDealAmountCents} cents.");

            var deal = new Deal(
                Guid.NewGuid(), reference, applicantName, fundingType,
                dealAmountCents, buyerName, buyerSector, timestamp);

            deal._auditTrail.Add(new AuditEntry("create", null, DealStatus.Submitted, actorRole, timestamp));
            return deal;
        }

        /// <summary>
        /// Materializes a deal that already exists in a datastore (or seed file) in
        /// a given lifecycle state, without replaying the transitions. Consistency
        /// invariants are enforced; funded amounts are recomputed with the canonical
        /// money maths so a seed can never disagree with the fee rules.
        /// </summary>
        public static Deal Restore(
            string reference,
            string applicantName,
            FundingType fundingType,
            long dealAmountCents,
            string buyerName,
            BuyerSector buyerSector,
            DealStatus status,
            IEnumerable<DealDocument> documents,
            DealTerms? terms,
            string? declineReason,
            DateOnly? fundedOn,
            DateTimeOffset timestamp)
        {
            if (dealAmountCents < MinimumDealAmountCents)
                throw new InvalidDataException(
                    $"Deal {reference}: amount {dealAmountCents} is below the minimum of {MinimumDealAmountCents} cents.");
            if (status is DealStatus.Approved or DealStatus.Funded or DealStatus.Settled && terms is null)
                throw new InvalidDataException($"Deal {reference}: status {status.ToWire()} requires terms.");
            if (status is DealStatus.Funded or DealStatus.Settled && fundedOn is null)
                throw new InvalidDataException($"Deal {reference}: status {status.ToWire()} requires funded_on.");
            if (status is DealStatus.Declined && string.IsNullOrWhiteSpace(declineReason))
                throw new InvalidDataException($"Deal {reference}: status DECLINED requires decline_reason.");

            var deal = new Deal(
                Guid.NewGuid(), reference, applicantName, fundingType,
                dealAmountCents, buyerName, buyerSector, timestamp);

            deal._documents.AddRange(documents);

            if (status is DealStatus.Approved or DealStatus.Funded or DealStatus.Settled)
                deal.Terms = terms;

            if (status is DealStatus.Funded or DealStatus.Settled)
            {
                deal.FundedOn = fundedOn;
                deal.FundedAmountCents = MoneyMath.ApplyBps(dealAmountCents, terms!.AdvanceRateBps);
                deal.TotalFeeCents = FeeSchedule.TotalFeeCents(
                    deal.FundedAmountCents.Value, terms.FacilityFeeRateBps, fundedOn!.Value, terms.ExpectedSettlementDate);
                deal.TotalRepayableCents = deal.FundedAmountCents.Value + deal.TotalFeeCents.Value;
            }

            if (status is DealStatus.Settled)
                deal._repayments.Add(new Repayment(Guid.NewGuid(), deal.TotalRepayableCents!.Value, timestamp));

            if (status is DealStatus.Declined)
                deal.DeclineReason = declineReason!.Trim();

            deal.Status = status;
            deal._auditTrail.Add(new AuditEntry("seed", null, status, "system", timestamp));
            return deal;
        }

        public Result AttachDocument(DocumentType docType, string filename)
        {
            if (string.IsNullOrWhiteSpace(filename))
                return Result.Failure(Error.Validation("filename is required."));

            _documents.Add(new DealDocument(docType, filename.Trim()));
            return Result.Success();
        }

        /// <summary>SUBMITTED → UNDER_REVIEW.</summary>
        public Result StartReview(string actorRole, DateTimeOffset timestamp)
        {
            var allowed = DealStateMachine.EnsureCanApply(Status, DealAction.Review);
            if (allowed.IsFailure)
                return allowed;

            ApplyTransition("review", DealStatus.UnderReview, actorRole, timestamp);
            return Result.Success();
        }

        /// <summary>
        /// UNDER_REVIEW → APPROVED. Validates the terms and the document gate; on a
        /// missing-documents failure the error lists the missing types and no
        /// transition happens.
        /// </summary>
        public Result Approve(
            int advanceRateBps,
            int facilityFeeRateBps,
            DateOnly expectedSettlementDate,
            DateOnly today,
            string actorRole,
            DateTimeOffset timestamp)
        {
            var allowed = DealStateMachine.EnsureCanApply(Status, DealAction.Approve);
            if (allowed.IsFailure)
                return allowed;

            var errors = new List<string>();
            if (advanceRateBps is < 1 or > MoneyMath.BpsScale)
                errors.Add($"advance_rate_bps must be between 1 and {MoneyMath.BpsScale}.");
            if (facilityFeeRateBps < 0)
                errors.Add("facility_fee_rate_bps must be zero or greater.");
            if (expectedSettlementDate <= today)
                errors.Add("expected_settlement_date must be a date after today.");
            if (errors.Count > 0)
                return Result.Failure(Error.Validation("Approval terms are invalid.", errors));

            var missing = DocumentRequirements.MissingFor(this);
            if (missing.Count > 0)
                return Result.Failure(Error.MissingDocuments(missing.Select(m => m.ToWire()).ToList()));

            Terms = new DealTerms(advanceRateBps, facilityFeeRateBps, expectedSettlementDate);
            ApplyTransition("approve", DealStatus.Approved, actorRole, timestamp);
            return Result.Success();
        }

        /// <summary>SUBMITTED or UNDER_REVIEW → DECLINED (terminal).</summary>
        public Result Decline(string? reason, string actorRole, DateTimeOffset timestamp)
        {
            var allowed = DealStateMachine.EnsureCanApply(Status, DealAction.Decline);
            if (allowed.IsFailure)
                return allowed;

            if (string.IsNullOrWhiteSpace(reason))
                return Result.Failure(Error.Validation("reason is required."));

            DeclineReason = reason.Trim();
            ApplyTransition("decline", DealStatus.Declined, actorRole, timestamp);
            return Result.Success();
        }

        /// <summary>
        /// APPROVED → FUNDED. Fixes the advance and the total facility fee at
        /// funding time:
        ///   funded_amount = round_half_up(deal_amount * advance_rate_bps / 10000)
        ///   total_fee     = round_half_up(funded_amount * facility_fee_rate_bps / 10000) * periods
        /// </summary>
        public Result Fund(DateOnly fundedOn, string actorRole, DateTimeOffset timestamp)
        {
            var allowed = DealStateMachine.EnsureCanApply(Status, DealAction.Fund);
            if (allowed.IsFailure)
                return allowed;

            var terms = Terms!; // set on approval, which is the only way into APPROVED
            if (fundedOn >= terms.ExpectedSettlementDate)
                return Result.Failure(Error.Validation(
                    "funded_on must be before the expected_settlement_date agreed at approval."));

            FundedOn = fundedOn;
            FundedAmountCents = MoneyMath.ApplyBps(DealAmountCents, terms.AdvanceRateBps);
            TotalFeeCents = FeeSchedule.TotalFeeCents(
                FundedAmountCents.Value, terms.FacilityFeeRateBps, fundedOn, terms.ExpectedSettlementDate);
            TotalRepayableCents = FundedAmountCents.Value + TotalFeeCents.Value;

            ApplyTransition("fund", DealStatus.Funded, actorRole, timestamp);
            return Result.Success();
        }

        /// <summary>
        /// Records a repayment against a FUNDED deal. Overpayment beyond the
        /// outstanding balance is rejected; when the balance reaches zero the deal
        /// automatically transitions to SETTLED.
        /// </summary>
        public Result<Repayment> RecordRepayment(long amountCents, string actorRole, DateTimeOffset timestamp)
        {
            if (Status != DealStatus.Funded)
                return Result.Failure<Repayment>(Error.Validation(
                    $"Repayments can only be recorded against a FUNDED deal; this deal is {Status.ToWire()}."));

            if (amountCents <= 0)
                return Result.Failure<Repayment>(Error.Validation("amount_cents must be a positive integer."));

            if (amountCents > OutstandingCents)
                return Result.Failure<Repayment>(Error.Validation(
                    "Repayment exceeds the outstanding balance.",
                    new Dictionary<string, object?> { ["outstanding_cents"] = OutstandingCents }));

            var repayment = new Repayment(Guid.NewGuid(), amountCents, timestamp);
            _repayments.Add(repayment);

            if (OutstandingCents == 0)
                ApplyTransition("settle", DealStatus.Settled, actorRole, timestamp);

            return Result.Success(repayment);
        }

        private void ApplyTransition(string action, DealStatus to, string actorRole, DateTimeOffset timestamp)
        {
            _auditTrail.Add(new AuditEntry(action, Status, to, actorRole, timestamp));
            Status = to;
        }
    }
}
