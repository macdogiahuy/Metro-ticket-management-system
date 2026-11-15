namespace Metro.Domain.Common;

/// <summary>
/// Represents a half-open UTC time interval [StartUtc, EndExclusiveUtc).
/// The lower bound is inclusive; the upper bound is exclusive.
/// Adheres to project rule: "Thời gian UTC; EndExclusive; không dùng DateTime.Now."
/// </summary>
public readonly record struct TimeWindow
{
    public DateTime StartUtc { get; }
    public DateTime? EndExclusiveUtc { get; }

    public TimeWindow(DateTime startUtc, DateTime? endExclusiveUtc = null)
    {
        var start = NormalizeToUtc(startUtc);
        DateTime? end = null;

        if (endExclusiveUtc.HasValue)
        {
            var normalizedEnd = NormalizeToUtc(endExclusiveUtc.Value);
            if (normalizedEnd <= start)
            {
                throw new ArgumentException(
                    $"EndExclusiveUtc ({normalizedEnd:O}) must be strictly greater than StartUtc ({start:O}).",
                    nameof(endExclusiveUtc));
            }
            end = normalizedEnd;
        }

        StartUtc = start;
        EndExclusiveUtc = end;
    }

    private static DateTime NormalizeToUtc(DateTime dt)
    {
        return dt.Kind switch
        {
            DateTimeKind.Utc => dt,
            DateTimeKind.Local => dt.ToUniversalTime(),
            _ => DateTime.SpecifyKind(dt, DateTimeKind.Utc)
        };
    }

    /// <summary>
    /// Checks whether the given timestamp falls within [StartUtc, EndExclusiveUtc).
    /// </summary>
    public bool Contains(DateTime timestampUtc)
    {
        var utc = NormalizeToUtc(timestampUtc);
        return utc >= StartUtc && (!EndExclusiveUtc.HasValue || utc < EndExclusiveUtc.Value);
    }

    /// <summary>
    /// Checks whether this interval overlaps with another [Start, EndExclusive) interval.
    /// Overlap condition: Start &lt; Other.EndExclusive and Other.Start &lt; EndExclusive.
    /// </summary>
    public bool Overlaps(TimeWindow other)
    {
        var startsBeforeOtherEnds = !other.EndExclusiveUtc.HasValue || StartUtc < other.EndExclusiveUtc.Value;
        var otherStartsBeforeEnds = !EndExclusiveUtc.HasValue || other.StartUtc < EndExclusiveUtc.Value;
        return startsBeforeOtherEnds && otherStartsBeforeEnds;
    }
}
