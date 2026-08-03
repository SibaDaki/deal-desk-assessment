using DealDesk.Domain.Enums;

namespace DealDesk.Domain.Entities
{
    /// <summary>One entry in a deal's status-transition audit trail.</summary>
    public sealed record AuditEntry(
        string Action,
        DealStatus? FromStatus,
        DealStatus ToStatus,
        string ActorRole,
        DateTimeOffset Timestamp);
}
