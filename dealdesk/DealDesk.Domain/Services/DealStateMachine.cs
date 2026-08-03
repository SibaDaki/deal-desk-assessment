using DealDesk.Domain.Common;
using DealDesk.Domain.Enums;

namespace DealDesk.Domain.Services
{
    public enum DealAction
    {
        Review = 0,
        Approve = 1,
        Decline = 2,
        Fund = 3
    }

    /// <summary>
    /// The single source of truth for legal deal transitions:
    ///
    ///   SUBMITTED --review--> UNDER_REVIEW --approve--> APPROVED --fund--> FUNDED --(fully repaid)--> SETTLED
    ///       |                     |
    ///       +-------decline------++----------> DECLINED
    ///
    /// SETTLED and DECLINED are terminal. Any other transition attempt is a conflict (HTTP 409).
    /// Settlement is not listed here because it is an automatic transition triggered by
    /// repayments reaching the total repayable amount, not a caller-invoked action.
    /// </summary>
    public static class DealStateMachine
    {
        private static readonly IReadOnlyDictionary<DealAction, DealStatus[]> AllowedSources =
            new Dictionary<DealAction, DealStatus[]>
            {
                [DealAction.Review] = new[] { DealStatus.Submitted },
                [DealAction.Approve] = new[] { DealStatus.UnderReview },
                [DealAction.Decline] = new[] { DealStatus.Submitted, DealStatus.UnderReview },
                [DealAction.Fund] = new[] { DealStatus.Approved }
            };

        public static Result EnsureCanApply(DealStatus current, DealAction action)
        {
            if (AllowedSources[action].Contains(current))
                return Result.Success();

            var allowed = string.Join(", ", AllowedSources[action].Select(s => s.ToWire()));
            return Result.Failure(Error.Conflict(
                $"Cannot {action.ToString().ToLowerInvariant()} a deal in status {current.ToWire()}; " +
                $"allowed from: {allowed}."));
        }
    }
}
