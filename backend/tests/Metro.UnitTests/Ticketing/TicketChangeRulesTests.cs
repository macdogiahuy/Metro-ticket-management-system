using Metro.Application.Abstractions.Ticketing;
using Metro.Application.Ticketing;
using Metro.Domain.Entities.Ticketing;

namespace Metro.UnitTests.Ticketing;

public sealed class TicketChangeRulesTests
{
    [Fact]
    public void Ticket_change_limit_reached_when_change_count_is_one_or_more()
    {
        var ex = Assert.Throws<TicketChangeException>(() =>
            TicketChangeRules.ValidateTicketEligibility("ACTIVE", null, false, 1));
        Assert.Equal(409, ex.Status);
        Assert.Equal("TICKET_CHANGE_LIMIT_REACHED", ex.Code);
    }

    [Theory]
    [InlineData("USED")]
    [InlineData("EXPIRED")]
    [InlineData("REVOKED")]
    [InlineData("REFUNDED")]
    public void Inactive_or_used_status_throws_conflict(string status)
    {
        var ex = Assert.Throws<TicketChangeException>(() =>
            TicketChangeRules.ValidateTicketEligibility(status, null, false, 0));
        Assert.Equal(409, ex.Status);
        Assert.Equal("TICKET_ALREADY_USED", ex.Code);
    }

    [Fact]
    public void Ticket_with_first_used_at_timestamp_cannot_be_changed()
    {
        var ex = Assert.Throws<TicketChangeException>(() =>
            TicketChangeRules.ValidateTicketEligibility("ACTIVE", DateTime.UtcNow.AddHours(-1), false, 0));
        Assert.Equal(409, ex.Status);
        Assert.Equal("TICKET_ALREADY_USED", ex.Code);
    }

    [Fact]
    public void Ticket_with_open_journey_cannot_be_changed()
    {
        var ex = Assert.Throws<TicketChangeException>(() =>
            TicketChangeRules.ValidateTicketEligibility("ACTIVE", null, true, 0));
        Assert.Equal(409, ex.Status);
        Assert.Equal("TICKET_ALREADY_USED", ex.Code);
    }

    [Fact]
    public void Unused_issued_or_active_ticket_with_zero_changes_is_eligible()
    {
        TicketChangeRules.ValidateTicketEligibility("ACTIVE", null, false, 0);
        TicketChangeRules.ValidateTicketEligibility("ISSUED", null, false, 0);
    }

    [Theory]
    [InlineData("SERVICE_DISRUPTION", TicketChangeReasonType.ServiceDisruption)]
    [InlineData("SYSTEM_ERROR", TicketChangeReasonType.SystemError)]
    [InlineData("OTHER", TicketChangeReasonType.Other)]
    public void Valid_reason_types_are_normalized(string input, TicketChangeReasonType expected)
    {
        Assert.Equal(expected, TicketChangeRules.NormalizeReasonType(input));
    }

    [Fact]
    public void Invalid_reason_type_throws_bad_request()
    {
        var ex = Assert.Throws<TicketChangeException>(() =>
            TicketChangeRules.NormalizeReasonType("CHANGE_MIND"));
        Assert.Equal(400, ex.Status);
        Assert.Equal("INVALID_CHANGE_REASON_TYPE", ex.Code);
    }

    [Fact]
    public void Requested_date_in_the_past_throws_bad_request()
    {
        var today = new DateOnly(2026, 12, 5);
        var past = new DateOnly(2026, 12, 4);
        var ex = Assert.Throws<TicketChangeException>(() =>
            TicketChangeRules.ValidateRequestedServiceDate(past, today));
        Assert.Equal(400, ex.Status);
        Assert.Equal("SERVICE_DATE_IN_PAST", ex.Code);
    }

    [Fact]
    public void Requested_date_today_or_future_is_accepted()
    {
        var today = new DateOnly(2026, 12, 5);
        TicketChangeRules.ValidateRequestedServiceDate(today, today);
        TicketChangeRules.ValidateRequestedServiceDate(today.AddDays(1), today);
    }

    [Fact]
    public void Same_origin_and_destination_station_throws_bad_request()
    {
        var stationId = Guid.NewGuid();
        var ex = Assert.Throws<TicketChangeException>(() =>
            TicketChangeRules.ValidateStations(stationId, stationId));
        Assert.Equal(400, ex.Status);
        Assert.Equal("SAME_STATION_NOT_ALLOWED", ex.Code);
    }

    [Fact]
    public void Distinct_origin_and_destination_stations_are_accepted()
    {
        TicketChangeRules.ValidateStations(Guid.NewGuid(), Guid.NewGuid());
        TicketChangeRules.ValidateStations(null, Guid.NewGuid());
    }

    [Fact]
    public void Approving_non_requested_status_throws_conflict()
    {
        var ex = Assert.Throws<TicketChangeException>(() =>
            TicketChangeRules.ValidateApproval("APPROVED", "Reason"));
        Assert.Equal(409, ex.Status);
        Assert.Equal("INVALID_REQUEST_STATUS", ex.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Approving_without_decision_reason_throws_bad_request(string? reason)
    {
        var ex = Assert.Throws<TicketChangeException>(() =>
            TicketChangeRules.ValidateApproval("REQUESTED", reason));
        Assert.Equal(400, ex.Status);
        Assert.Equal("DECISION_REASON_REQUIRED", ex.Code);
    }
}
