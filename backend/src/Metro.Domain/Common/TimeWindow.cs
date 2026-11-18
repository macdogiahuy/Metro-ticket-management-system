namespace Metro.Domain.Common;

/// <summary>
/// Represents a half-open UTC time interval [StartUtc, EndExclusiveUtc).
/// The lower bound is inclusive; the upper bound is exclusive.
/// Adheres to project rule: "Thời gian UTC; EndExclusive; không dùng DateTime.Now."
/// </summary>
public readonly record struct TimeWindow
{
    public DateTime? StartUtc { get; }
    public DateTime? EndExclusiveUtc { get; }

    public TimeWindow(DateTime? startUtc, DateTime? endExclusiveUtc = null)
    {
        DateTime? start = startUtc.HasValue ? NormalizeToUtc(startUtc.Value) : null;
        DateTime? end = null;

        if (endExclusiveUtc.HasValue)
        {
            var normalizedEnd = NormalizeToUtc(endExclusiveUtc.Value);
            if (start.HasValue && normalizedEnd <= start.Value)
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

    public static DateTime NormalizeToUtc(DateTime dt)
    {
        return dt.Kind switch
        {
            DateTimeKind.Utc => dt,
            DateTimeKind.Local => dt.ToUniversalTime(),
            _ => throw new ArgumentException("A timestamp must specify UTC or Local kind.", nameof(dt))
        };
    }

    /// <summary>
    /// Checks whether the given timestamp falls within [StartUtc, EndExclusiveUtc).
    /// </summary>
    public bool Contains(DateTime timestampUtc)
    {
        var utc = NormalizeToUtc(timestampUtc);
        return (!StartUtc.HasValue || utc >= StartUtc.Value) && (!EndExclusiveUtc.HasValue || utc < EndExclusiveUtc.Value);
    }

    /// <summary>
    /// Checks whether this interval overlaps with another [Start, EndExclusive) interval.
    /// Overlap condition: Start &lt; Other.EndExclusive and Other.Start &lt; EndExclusive.
    /// </summary>
    public bool Overlaps(TimeWindow other)
    {
        var startsBeforeOtherEnds = !other.EndExclusiveUtc.HasValue || !StartUtc.HasValue || StartUtc.Value < other.EndExclusiveUtc.Value;
        var otherStartsBeforeEnds = !EndExclusiveUtc.HasValue || !other.StartUtc.HasValue || other.StartUtc.Value < EndExclusiveUtc.Value;
        return startsBeforeOtherEnds && otherStartsBeforeEnds;
    }
}

