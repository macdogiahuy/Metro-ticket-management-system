using Metro.Application.Abstractions.Booking;
using Metro.Application.Booking;

namespace Metro.UnitTests.Booking;

public sealed class BookingRulesTests
{
    [Theory]
    [InlineData(7, true)]
    [InlineData(8, false)]
    [InlineData(120, false)]
    [InlineData(121, true)]
    public void Idempotency_keys_enforce_the_published_length_bounds(int length, bool invalid)
    {
        var key = new string('a', length);
        if (invalid) Assert.Throws<BookingException>(() => BookingRules.ValidateKey(key));
        else BookingRules.ValidateKey(key);
    }

    [Theory]
    [InlineData("abcdefgh ")]
    [InlineData("abcdefgh\n")]
    [InlineData("abcdefghé")]
    public void Idempotency_keys_reject_whitespace_controls_and_non_ASCII(string key) =>
        Assert.Throws<BookingException>(() => BookingRules.ValidateKey(key));

    [Fact]
    public void Selection_accepts_day_seven_but_passes_cannot_carry_route_or_date()
    {
        var today = new DateOnly(2026, 11, 24);
        var single = new BookingSelection(Guid.NewGuid(), ServiceDate: today.AddDays(7),
            OriginStationId: Guid.NewGuid(), DestinationStationId: Guid.NewGuid());
        BookingRules.ValidateSelection(single, true, today, today);
        Assert.Throws<BookingException>(() => BookingRules.ValidateSelection(single, false, today, today));
        Assert.Throws<BookingException>(() => BookingRules.ValidateSelection(single with { DestinationStationId = single.OriginStationId }, true, today, today));
    }
    [Fact]
    public void Deadline_uses_utc_plus_seven_midnight_and_caps_at_24_hours()
    {
        var start = new DateTimeOffset(2026, 11, 23, 16, 59, 0, TimeSpan.Zero);
        Assert.Equal(start.AddMinutes(1), BookingRules.Deadline(start, new DateOnly(2026, 11, 23)));
        Assert.Equal(start.AddHours(24), BookingRules.Deadline(start, new DateOnly(2026, 11, 24)));
        Assert.Equal(start.AddHours(24), BookingRules.Deadline(start, null));
    }

    [Fact]
    public void Revision_cannot_extend_original_seven_day_window()
    {
        var day = new DateOnly(2026, 11, 23);
        var request = new BookingSelection(Guid.NewGuid(), ServiceDate: day.AddDays(8),
            OriginStationId: Guid.NewGuid(), DestinationStationId: Guid.NewGuid());
        Assert.Equal("TRAVEL_DATE_OUT_OF_RANGE", Assert.Throws<BookingException>(
            () => BookingRules.ValidateSelection(request, true, day.AddDays(1), day)).Code);
    }
}
