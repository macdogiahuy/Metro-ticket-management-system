using Metro.Application.Abstractions.Booking;
using Metro.Application.Ticketing;

namespace Metro.Application.Booking;

public static class BookingRules
{
    public static DateTimeOffset Deadline(DateTimeOffset start, DateOnly? serviceDate)
    {
        var deadline = start.AddHours(24);
        if (serviceDate is not { } day) return deadline;
        var end = new DateTimeOffset(TicketRules.Midnight(day.AddDays(1)));
        return end < deadline ? end : deadline;
    }

    public static void ValidateSelection(BookingSelection selection, bool singleTrip, DateOnly today, DateOnly orderDate)
    {
        if (!singleTrip)
        {
            if (selection.ServiceDate is not null || selection.OriginStationId is not null ||
                selection.DestinationStationId is not null || selection.JourneyOptionIndex is not null)
                throw new BookingException("PERIOD_PASS_HAS_NO_ROUTE_OR_DATE", 400);
            return;
        }
        if (selection.ServiceDate is not { } day || day < today || day > orderDate.AddDays(7))
            throw new BookingException("TRAVEL_DATE_OUT_OF_RANGE", 422);
        if (selection.OriginStationId is not { } origin || origin == Guid.Empty ||
            selection.DestinationStationId is not { } destination || destination == Guid.Empty ||
            origin == destination || selection.JourneyOptionIndex is <= 0)
            throw new BookingException("INVALID_BOOKING_ROUTE", 400);
    }

    public static void ValidateKey(string key)
    {
        if (key.Length is < 8 or > 120 || key.Any(c => c is < (char)33 or > (char)126))
            throw new BookingException("INVALID_IDEMPOTENCY_KEY", 400);
    }
}
