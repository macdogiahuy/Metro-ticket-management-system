namespace Metro.Domain.Common;

/// <summary>
/// Represents a half-open date interval [Start, EndExclusive).
/// The lower bound is inclusive; the upper bound is exclusive.
/// </summary>
public readonly record struct DateWindow
{
    public DateOnly Start { get; }
    public DateOnly? EndExclusive { get; }

    public DateWindow(DateOnly start, DateOnly? endExclusive = null)
    {
        if (endExclusive.HasValue && endExclusive.Value <= start)
        {
            throw new ArgumentException(
                $"EndExclusive ({endExclusive.Value:yyyy-MM-dd}) must be strictly greater than Start ({start:yyyy-MM-dd}).",
                nameof(endExclusive));
        }

        Start = start;
        EndExclusive = endExclusive;
    }

    public bool Contains(DateOnly date) =>
        date >= Start && (!EndExclusive.HasValue || date < EndExclusive.Value);

    public bool Overlaps(DateWindow other)
    {
        var startsBeforeOtherEnds = !other.EndExclusive.HasValue || Start < other.EndExclusive.Value;
        var otherStartsBeforeEnds = !EndExclusive.HasValue || other.Start < EndExclusive.Value;
        return startsBeforeOtherEnds && otherStartsBeforeEnds;
    }
}
