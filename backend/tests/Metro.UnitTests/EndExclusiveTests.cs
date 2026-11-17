using Metro.Domain.Common;
using Metro.Domain.Entities.Identity;
using Metro.Domain.Entities.Network;
using Metro.Domain.Entities.Orders;
using Metro.Domain.Entities.Tickets;

namespace Metro.UnitTests;

public class EndExclusiveTests
{
    [Fact]
    public void TimeWindow_Contains_InclusiveStart_And_ExclusiveEnd()
    {
        var start = new DateTime(2026, 11, 16, 8, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 11, 16, 10, 0, 0, DateTimeKind.Utc);
        var window = new TimeWindow(start, end);

        // Exact start is INCLUSIVE
        Assert.True(window.Contains(start));

        // Exact end is EXCLUSIVE (not contained)
        Assert.False(window.Contains(end));

        // Point strictly inside is contained
        var midPoint = new DateTime(2026, 11, 16, 9, 0, 0, DateTimeKind.Utc);
        Assert.True(window.Contains(midPoint));

        // Just one millisecond before end is contained
        Assert.True(window.Contains(end.AddMilliseconds(-1)));

        // Just one millisecond after end is NOT contained
        Assert.False(window.Contains(end.AddMilliseconds(1)));

        // Timestamp before start is NOT contained
        Assert.False(window.Contains(start.AddSeconds(-1)));
    }

    [Fact]
    public void TimeWindow_AbuttingIntervals_DoNotOverlap()
    {
        // Interval 1: [08:00, 10:00)
        // Interval 2: [10:00, 12:00)
        // In EndExclusive semantics, abutting intervals share only the boundary point,
        // which belongs to Interval 2, not Interval 1. They DO NOT overlap.
        var window1 = new TimeWindow(
            new DateTime(2026, 11, 16, 8, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 11, 16, 10, 0, 0, DateTimeKind.Utc));

        var window2 = new TimeWindow(
            new DateTime(2026, 11, 16, 10, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc));

        Assert.False(window1.Overlaps(window2));
        Assert.False(window2.Overlaps(window1));
    }

    [Fact]
    public void TimeWindow_OverlappingIntervals_ReturnTrue()
    {
        // Interval 1: [08:00, 10:30)
        // Interval 2: [10:00, 12:00)
        var window1 = new TimeWindow(
            new DateTime(2026, 11, 16, 8, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 11, 16, 10, 30, 0, DateTimeKind.Utc));

        var window2 = new TimeWindow(
            new DateTime(2026, 11, 16, 10, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc));

        Assert.True(window1.Overlaps(window2));
        Assert.True(window2.Overlaps(window1));
    }

    [Fact]
    public void TimeWindow_Throws_WhenEndLessThanOrEqualToStart()
    {
        var start = new DateTime(2026, 11, 16, 8, 0, 0, DateTimeKind.Utc);

        // Equal start and end
        Assert.Throws<ArgumentException>(() => new TimeWindow(start, start));

        // End before start
        Assert.Throws<ArgumentException>(() => new TimeWindow(start, start.AddHours(-1)));
    }

    [Fact]
    public void TimeWindow_OpenEnded_ContainsAllFutureInstants()
    {
        var start = new DateTime(2026, 11, 16, 8, 0, 0, DateTimeKind.Utc);
        var openEnded = new TimeWindow(start, null);

        Assert.True(openEnded.Contains(start));
        Assert.True(openEnded.Contains(start.AddYears(10)));
        Assert.False(openEnded.Contains(start.AddSeconds(-1)));
    }

    [Fact]
    public void DateWindow_Contains_InclusiveStart_And_ExclusiveEnd()
    {
        var start = new DateOnly(2026, 11, 16);
        var end = new DateOnly(2026, 11, 20);
        var window = new DateWindow(start, end);

        Assert.True(window.Contains(new DateOnly(2026, 11, 16)));
        Assert.True(window.Contains(new DateOnly(2026, 11, 19)));
        Assert.False(window.Contains(new DateOnly(2026, 11, 20))); // Exclusive
        Assert.False(window.Contains(new DateOnly(2026, 11, 15)));
    }

    [Fact]
    public void Ticket_Validity_EndExclusive_Rule()
    {
        var effectiveFrom = new DateTime(2026, 11, 16, 5, 0, 0, DateTimeKind.Utc);
        var expiresAt = new DateTime(2026, 11, 16, 23, 0, 0, DateTimeKind.Utc);

        var ticket = new Ticket
        {
            Status = "ACTIVE",
            EffectiveFrom = effectiveFrom,
            ExpiresAt = expiresAt
        };

        // Exactly at effective_from: valid
        Assert.True(ticket.IsValidAt(effectiveFrom));

        // 1 millisecond before expiry: valid
        Assert.True(ticket.IsValidAt(expiresAt.AddMilliseconds(-1)));

        // Exactly at expires_at: EXPIRED (EndExclusive)
        Assert.False(ticket.IsValidAt(expiresAt));

        // After expires_at: expired
        Assert.False(ticket.IsValidAt(expiresAt.AddSeconds(1)));

        // Non-active status: invalid regardless of time
        ticket.Status = "USED";
        Assert.False(ticket.IsValidAt(effectiveFrom.AddHours(2)));
    }

    [Fact]
    public void Ticket_OverlapsValidity_Enforces_NoOverlappingValidity()
    {
        var ticketA = new Ticket
        {
            Status = "ACTIVE",
            EffectiveFrom = new DateTime(2026, 11, 16, 8, 0, 0, DateTimeKind.Utc),
            ExpiresAt = new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc)
        };

        // Ticket B is consecutive: [12:00, 16:00) -> NO overlap
        var ticketB = new Ticket
        {
            Status = "ACTIVE",
            EffectiveFrom = new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc),
            ExpiresAt = new DateTime(2026, 11, 16, 16, 0, 0, DateTimeKind.Utc)
        };

        Assert.False(ticketA.OverlapsValidity(ticketB));

        // Ticket C overlaps: [11:00, 15:00)
        var ticketC = new Ticket
        {
            Status = "ACTIVE",
            EffectiveFrom = new DateTime(2026, 11, 16, 11, 0, 0, DateTimeKind.Utc),
            ExpiresAt = new DateTime(2026, 11, 16, 15, 0, 0, DateTimeKind.Utc)
        };

        Assert.True(ticketA.OverlapsValidity(ticketC));
    }

    [Fact]
    public void Order_IsHoldExpired_Follows_EndExclusive()
    {
        var holdExpiresAt = new DateTime(2026, 11, 16, 10, 0, 0, DateTimeKind.Utc);
        var order = new Order
        {
            HoldExpiresAt = holdExpiresAt
        };

        // Before expiry: not expired
        Assert.False(order.IsHoldExpired(holdExpiresAt.AddSeconds(-1)));

        // At exact expiry instant: expired (EndExclusive boundary)
        Assert.True(order.IsHoldExpired(holdExpiresAt));

        // After expiry: expired
        Assert.True(order.IsHoldExpired(holdExpiresAt.AddSeconds(1)));
    }

    [Fact]
    public void ScheduleVersion_IsEffectiveAt_Follows_EndExclusive()
    {
        var effectiveFrom = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        var effectiveTo = new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc);

        var schedule = new ScheduleVersion
        {
            Status = "PUBLISHED",
            EffectiveFrom = effectiveFrom,
            EffectiveTo = effectiveTo
        };

        Assert.True(schedule.IsEffectiveAt(effectiveFrom));
        Assert.True(schedule.IsEffectiveAt(effectiveTo.AddMilliseconds(-1)));
        Assert.False(schedule.IsEffectiveAt(effectiveTo)); // EndExclusive
        Assert.False(schedule.IsEffectiveAt(effectiveFrom.AddDays(-1)));

        // When not published, never effective
        schedule.Status = "DRAFT";
        Assert.False(schedule.IsEffectiveAt(effectiveFrom.AddDays(5)));
    }

    [Fact]
    public void AccountRoleAssignment_IsActiveAt_Checks_TimeWindow_And_Revocation()
    {
        var validFrom = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        var validTo = new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc);

        var assignment = new AccountRoleAssignment
        {
            ValidFrom = validFrom,
            ValidTo = validTo
        };

        var midPoint = new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc);
        Assert.True(assignment.IsActiveAt(midPoint));

        // At exact validTo: NOT active (EndExclusive)
        Assert.False(assignment.IsActiveAt(validTo));

        // Revoked assignment: NOT active even within valid window
        assignment.RevokedAt = midPoint.AddDays(-1);
        Assert.False(assignment.IsActiveAt(midPoint));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Ticket_Null_Bounds_Are_Independent(bool hasStart, bool hasEnd)
    {
        var start = new DateTime(2026, 11, 16, 8, 0, 0, DateTimeKind.Utc);
        var end = start.AddHours(2);
        var ticket = new Ticket { Status = "ACTIVE", EffectiveFrom = hasStart ? start : null, ExpiresAt = hasEnd ? end : null };
        Assert.True(ticket.IsValidAt(start));
        Assert.Equal(!hasStart, ticket.IsValidAt(start.AddTicks(-1)));
        Assert.Equal(!hasEnd, ticket.IsValidAt(end));
        var after = new Ticket { EffectiveFrom = end, ExpiresAt = end.AddHours(1) };
        Assert.Equal(!hasEnd, ticket.OverlapsValidity(after));
        Assert.Equal(!hasEnd, after.OverlapsValidity(ticket));
    }

    [Fact]
    public void Calendar_Empty_And_Abutting_Date_Ranges_Do_Not_Overlap()
    {
        var day = new DateOnly(2026, 11, 16);
        var empty = new DateWindow(day, day);
        var surrounding = new DateWindow(day.AddDays(-1), day.AddDays(1));
        Assert.False(empty.Contains(day));
        Assert.False(empty.Overlaps(surrounding));
        Assert.False(surrounding.Overlaps(empty));
        Assert.False(new DateWindow(day.AddDays(-1), day).Overlaps(new DateWindow(day)));
        Assert.Throws<ArgumentException>(() => new DateWindow(day, day.AddDays(-1)));
        Assert.False(new ServiceCalendar { Status = "PUBLISHED", EffectiveFrom = day, EffectiveTo = day }.IsEffectiveOn(day));
    }

    [Fact]
    public void Utc_Helper_Rejects_Unspecified_At_Every_Input()
    {
        var utc = new DateTime(2026, 11, 16, 8, 0, 0, DateTimeKind.Utc);
        var unspecified = DateTime.SpecifyKind(utc, DateTimeKind.Unspecified);
        Assert.Throws<ArgumentException>(() => new TimeWindow(unspecified));
        Assert.Throws<ArgumentException>(() => new TimeWindow(utc, unspecified));
        Assert.Throws<ArgumentException>(() => new TimeWindow(null, null).Contains(unspecified));
        Assert.Equal(utc, new TimeWindow(utc.ToLocalTime()).StartUtc);
        Assert.True(new TimeWindow(utc, utc.AddHours(1)).Contains(utc.ToLocalTime()));
        Assert.Throws<ArgumentException>(() => new Order { HoldExpiresAt = utc }.IsHoldExpired(unspecified));
    }
}

