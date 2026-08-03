namespace DealDesk.Application.Abstractions
{
    /// <summary>
    /// Injectable clock so date-sensitive rules (e.g. "expected_settlement_date
    /// must be after today") are deterministic under test.
    /// </summary>
    public interface IClock
    {
        DateOnly Today { get; }
        DateTimeOffset UtcNow { get; }
    }
}
