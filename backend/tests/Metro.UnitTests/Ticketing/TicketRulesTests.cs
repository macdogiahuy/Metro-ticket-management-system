using Metro.Application.Ticketing;

namespace Metro.UnitTests.Ticketing;

public sealed class TicketRulesTests
{
    [Theory]
    [InlineData("2020-11-21", "2026-11-20", true)]
    [InlineData("2020-11-20", "2026-11-20", false)]
    [InlineData("2026-11-21", "2026-11-20", false)]
    [InlineData("2020-02-29", "2026-02-27", true)]
    [InlineData("2020-02-29", "2026-02-28", false)]
    public void Child_age_is_evaluated_on_travel_date(string birth, string travel, bool expected) =>
        Assert.Equal(expected, TicketRules.IsChildUnderSix(DateOnly.Parse(birth), DateOnly.Parse(travel)));

    [Theory]
    [InlineData("DAY", "2026-11-20T17:00:00Z")]
    [InlineData("WEEK", "2026-11-26T17:00:00Z")]
    [InlineData("MONTH", "2026-12-19T17:00:00Z")]
    public void Pass_expiry_uses_calendar_days_in_UTC7(string product, string expiry)
    {
        var paid = DateTimeOffset.Parse("2026-11-20T01:23:45Z");
        var window = TicketRules.Validity(product, null, paid);
        Assert.Equal(paid.UtcDateTime, window.Start);
        Assert.Equal(DateTimeOffset.Parse(expiry).UtcDateTime, window.EndExclusive);
        Assert.True(TicketRules.Contains(window.Start, window.EndExclusive, window.EndExclusive.AddTicks(-1)));
        Assert.False(TicketRules.Contains(window.Start, window.EndExclusive, window.EndExclusive));
    }
    [Fact]
    public void Single_starts_at_local_midnight_not_payment_time()
    {
        var window = TicketRules.Validity("SINGLE_TRIP", new DateOnly(2026, 11, 22), DateTimeOffset.Parse("2026-11-20T17:15:00Z"));
        Assert.Equal(DateTime.Parse("2026-11-21T17:00:00Z").ToUniversalTime(), window.Start);
        Assert.Equal(window.Start.AddDays(1), window.EndExclusive);
        Assert.False(TicketRules.Contains(window.Start, window.EndExclusive, window.Start.AddTicks(-1)));
    }
    [Fact]
    public void SQL_created_ticket_waiting_for_QR_is_not_usable()
    {
        var ticket = new Metro.Domain.Entities.Tickets.Ticket { Status = "ISSUED" };
        Assert.False(ticket.IsValidAt(DateTime.UtcNow));
    }
}
