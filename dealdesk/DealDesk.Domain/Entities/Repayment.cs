namespace DealDesk.Domain.Entities
{
    public sealed record Repayment(Guid Id, long AmountCents, DateTimeOffset RecordedAt);
}
