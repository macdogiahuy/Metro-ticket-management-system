namespace Metro.Application.Ticketing;

public static class TicketRules
{
    // Project timezone is fixed UTC+7; no DST. All persisted instants are UTC.
    public static DateOnly LocalDate(DateTimeOffset instant) => DateOnly.FromDateTime(instant.ToOffset(TimeSpan.FromHours(7)).DateTime);
    public static DateTime Midnight(DateOnly date) => DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue).AddHours(-7), DateTimeKind.Utc);
    public static bool IsChildUnderSix(DateOnly birth, DateOnly travelDate) => birth <= travelDate && birth.AddYears(6) > travelDate;
    public static (DateTime Start, DateTime EndExclusive) Validity(string product, DateOnly? day, DateTimeOffset paidAt)
    {
        if (product == "SINGLE_TRIP" && day.HasValue) return (Midnight(day.Value), Midnight(day.Value.AddDays(1)));
        var days = product switch { "DAY" => 1, "WEEK" => 7, "MONTH" => 30, _ => throw new ArgumentException("Unsupported product") };
        return (paidAt.UtcDateTime, Midnight(LocalDate(paidAt).AddDays(days)));
    }
    public static bool Contains(DateTime? start, DateTime? end, DateTime now) => start <= now && now < end;
}
