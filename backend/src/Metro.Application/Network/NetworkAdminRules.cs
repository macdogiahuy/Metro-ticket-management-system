using Metro.Application.Abstractions.Network;
using Metro.Domain.Common;

namespace Metro.Application.Network;

public sealed record LineStopRuleInput(Guid StationId, short SequenceNo, int DistanceFromOriginM, int TravelTimeSeconds);
public sealed record TripStopTimeRuleInput(Guid StationId, short SequenceNo, DateTime PlannedArrivalAt, DateTime PlannedDepartureAt, int DwellSeconds);

/// <summary>Pure BE-13 validation rules for UC24, UC29, UC31, UC37/38 (BR-018, BR-050..057, AC-X18/X23/X24).</summary>
public static class NetworkAdminRules
{
    private static readonly HashSet<string> MasterStatuses = new(StringComparer.Ordinal) { "PLANNED", "ACTIVE", "SUSPENDED", "CLOSED" };
    private static readonly HashSet<string> StationTypes = new(StringComparer.Ordinal) { "AT_GRADE", "ELEVATED", "UNDERGROUND", "INTERCHANGE" };
    private static readonly HashSet<string> DayTypes = new(StringComparer.Ordinal) { "WEEKDAY", "WEEKEND", "HOLIDAY", "EVENT", "DISRUPTION" };
    private static readonly HashSet<string> CalendarStatuses = new(StringComparer.Ordinal) { "DRAFT", "PUBLISHED", "SUPERSEDED", "EXPIRED", "CANCELLED" };
    private static readonly HashSet<string> TripStatuses = new(StringComparer.Ordinal) { "PLANNED", "READY", "IN_SERVICE", "COMPLETED", "DELAYED", "CANCELLED", "DISRUPTED" };
    private static readonly HashSet<string> BoardingStatuses = new(StringComparer.Ordinal) { "OPEN", "PAUSED", "CLOSED" };
    private static readonly HashSet<string> TrainsetStatuses = new(StringComparer.Ordinal) { "AVAILABLE", "ASSIGNED", "IN_SERVICE", "MAINTENANCE", "OUT_OF_SERVICE" };

    public static string RequireText(string? value, int maxLen, string code, string message)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed.Length > maxLen) throw new NetworkAdminException(code, 400, message);
        return trimmed;
    }

    public static string NormalizeMasterStatus(string? status) =>
        NormalizeAllowed(status, MasterStatuses, "INVALID_STATUS", "Status must be one of: PLANNED, ACTIVE, SUSPENDED, CLOSED.");

    public static string NormalizeStationType(string? stationType) =>
        NormalizeAllowed(stationType, StationTypes, "INVALID_STATION_TYPE", "StationType must be one of: AT_GRADE, ELEVATED, UNDERGROUND, INTERCHANGE.");

    public static void ValidateCoordinates(decimal? latitude, decimal? longitude)
    {
        if (latitude is < -90m or > 90m)
            throw new NetworkAdminException("INVALID_COORDINATES", 400, "Latitude must be between -90 and 90.");
        if (longitude is < -180m or > 180m)
            throw new NetworkAdminException("INVALID_COORDINATES", 400, "Longitude must be between -180 and 180.");
    }

    public static void ValidateNonNegativeSeconds(int value, string code, string fieldName)
    {
        if (value < 0) throw new NetworkAdminException(code, 400, $"{fieldName} must be >= 0.");
    }

    public static short ValidateDirection(short direction) =>
        direction is 0 or 1 ? direction : throw new NetworkAdminException("INVALID_DIRECTION", 400, "Direction must be 0 or 1.");

    public static void ValidateLineStops(IReadOnlyList<LineStopRuleInput> stops)
    {
        if (stops is null || stops.Count == 0)
            throw new NetworkAdminException("EMPTY_LINE_STOPS", 400, "At least one station stop is required.");

        var ordered = stops.OrderBy(s => s.SequenceNo).ToArray();
        var seenStations = new HashSet<Guid>();
        var prevDistance = -1;
        for (var i = 0; i < ordered.Length; i++)
        {
            var s = ordered[i];
            if (s.SequenceNo != i + 1)
                throw new NetworkAdminException("INVALID_STOP_SEQUENCE", 400, "Station sequence numbers must be contiguous starting from 1.");
            if (s.StationId == Guid.Empty || !seenStations.Add(s.StationId))
                throw new NetworkAdminException("DUPLICATE_LINE_STATION", 400, "Each station may appear only once per line direction.");
            if (s.DistanceFromOriginM < 0 || s.DistanceFromOriginM < prevDistance)
                throw new NetworkAdminException("INVALID_STOP_DISTANCE", 400, "distanceFromOriginM must be non-negative and non-decreasing along the line.");
            if (s.TravelTimeSeconds < 0)
                throw new NetworkAdminException("INVALID_TRAVEL_TIME", 400, "travelTimeSeconds must be >= 0.");
            prevDistance = s.DistanceFromOriginM;
        }
    }

    public static string NormalizeDayType(string? dayType) =>
        NormalizeAllowed(dayType, DayTypes, "INVALID_DAY_TYPE", "DayType must be one of: WEEKDAY, WEEKEND, HOLIDAY, EVENT, DISRUPTION.");

    public static string NormalizeCalendarStatus(string? status) =>
        NormalizeAllowed(status, CalendarStatuses, "INVALID_CALENDAR_STATUS", "Calendar status must be DRAFT, PUBLISHED, SUPERSEDED, EXPIRED, or CANCELLED.");

    public static short[] ValidateDaysOfWeek(short[]? daysOfWeek)
    {
        if (daysOfWeek is null || daysOfWeek.Length == 0)
            throw new NetworkAdminException("INVALID_DAYS_OF_WEEK", 400, "daysOfWeek must contain at least one day (1..7).");
        if (daysOfWeek.Any(d => d is < 1 or > 7))
            throw new NetworkAdminException("INVALID_DAYS_OF_WEEK", 400, "daysOfWeek values must be between 1 (Monday) and 7 (Sunday).");
        return daysOfWeek.Distinct().OrderBy(d => d).ToArray();
    }

    public static void ValidateDateWindow(DateOnly effectiveFrom, DateOnly? effectiveTo)
    {
        if (effectiveTo.HasValue && effectiveTo.Value < effectiveFrom)
            throw new NetworkAdminException("INVALID_EFFECTIVE_WINDOW", 400, "effectiveTo must be on or after effectiveFrom.");
    }

    public static (DateTime FromUtc, DateTime? ToUtc) ValidateEffectiveWindow(DateTime from, DateTime? to)
    {
        var fromUtc = ToUtc(from);
        var toUtc = to.HasValue ? ToUtc(to.Value) : (DateTime?)null;
        try
        {
            var win = new TimeWindow(fromUtc, toUtc);
            return (win.StartUtc!.Value, win.EndExclusiveUtc);
        }
        catch (ArgumentException ex)
        {
            throw new NetworkAdminException("INVALID_EFFECTIVE_WINDOW", 400, ex.Message);
        }
    }

    public static void EnsureMakerChecker(string createdByUserId, string actorUserId, string entityLabel)
    {
        if (string.Equals(createdByUserId, actorUserId, StringComparison.Ordinal))
            throw new NetworkAdminException("MAKER_CHECKER_VIOLATION", 409, $"Creator cannot approve or publish their own {entityLabel}.");
    }

    public static string NormalizeTripStatus(string? status) =>
        NormalizeAllowed(status, TripStatuses, "INVALID_TRIP_STATUS", "Trip status must be PLANNED, READY, IN_SERVICE, COMPLETED, DELAYED, CANCELLED, or DISRUPTED.");

    public static string NormalizeBoardingStatus(string? status) =>
        NormalizeAllowed(status, BoardingStatuses, "INVALID_BOARDING_STATUS", "Boarding status must be OPEN, PAUSED, or CLOSED.");

    public static void ValidateTripStatusTransition(string currentStatus, string nextStatus, string? cancellationReason)
    {
        if (nextStatus == "CANCELLED" && string.IsNullOrWhiteSpace(cancellationReason))
            throw new NetworkAdminException("CANCELLATION_REASON_REQUIRED", 400, "cancellationReason is required when cancelling a trip.");
        if (currentStatus == nextStatus) return;
        if (currentStatus is "COMPLETED" or "CANCELLED")
            throw new NetworkAdminException("INVALID_TRIP_TRANSITION", 409, $"Cannot transition trip from terminal status '{currentStatus}' to '{nextStatus}'.");
        if (currentStatus is "IN_SERVICE" or "DELAYED" or "DISRUPTED" && nextStatus is "PLANNED" or "READY")
            throw new NetworkAdminException("INVALID_TRIP_TRANSITION", 409, $"Cannot revert running trip from '{currentStatus}' to '{nextStatus}'.");
    }

    public static void ValidateTripStopTimes(DateTime plannedStartUtc, DateTime plannedEndUtc, IReadOnlyList<TripStopTimeRuleInput> stops)
    {
        if (plannedEndUtc <= plannedStartUtc)
            throw new NetworkAdminException("INVALID_TRIP_WINDOW", 400, "plannedEndAt must be strictly after plannedStartAt.");
        if (stops.Count == 0) return;

        var ordered = stops.OrderBy(s => s.SequenceNo).ToArray();
        var seen = new HashSet<Guid>();
        DateTime? prevDeparture = null;
        for (var i = 0; i < ordered.Length; i++)
        {
            var s = ordered[i];
            var arr = ToUtc(s.PlannedArrivalAt);
            var dep = ToUtc(s.PlannedDepartureAt);
            if (s.SequenceNo != i + 1)
                throw new NetworkAdminException("INVALID_STOP_SEQUENCE", 400, "Trip stop sequence numbers must be contiguous starting from 1.");
            if (s.StationId == Guid.Empty || !seen.Add(s.StationId))
                throw new NetworkAdminException("DUPLICATE_STOP_STATION", 400, "Each station may appear only once per trip.");
            if (s.DwellSeconds < 0 || dep < arr)
                throw new NetworkAdminException("INVALID_STOP_TIME", 400, "plannedDepartureAt must be >= plannedArrivalAt and dwellSeconds >= 0.");
            if (arr < plannedStartUtc || dep > plannedEndUtc)
                throw new NetworkAdminException("STOP_OUTSIDE_TRIP_WINDOW", 400, "Stop times must fall within [plannedStartAt, plannedEndAt].");
            if (prevDeparture.HasValue && arr < prevDeparture.Value)
                throw new NetworkAdminException("NON_MONOTONIC_STOP_TIME", 400, "Stop arrival times must be non-decreasing along the trip.");
            prevDeparture = dep;
        }
    }

    public static string NormalizeTrainsetStatus(string? status) =>
        NormalizeAllowed(status, TrainsetStatuses, "INVALID_TRAINSET_STATUS", "Trainset status must be AVAILABLE, ASSIGNED, IN_SERVICE, MAINTENANCE, or OUT_OF_SERVICE.");

    public static decimal ValidateMaxLoadKg(decimal maxLoadKg)
    {
        if (maxLoadKg <= 0m || maxLoadKg > 99_999_999.9m || decimal.Round(maxLoadKg, 1) != maxLoadKg)
            throw new NetworkAdminException("INVALID_MAX_LOAD_KG", 400, "maxLoadKg must be > 0 with at most 1 decimal place (BR-054).");
        return maxLoadKg;
    }

    public static decimal ValidateObservedLoadKg(decimal observedLoadKg)
    {
        if (observedLoadKg < 0m || observedLoadKg > 9_999_999.9m || decimal.Round(observedLoadKg, 1) != observedLoadKg)
            throw new NetworkAdminException("INVALID_LOAD_KG", 400, "observedLoadKg must be >= 0 with at most 1 decimal place (BR-054).");
        return observedLoadKg;
    }

    public static DateTime ValidateObservedAt(DateTime observedAt, DateTime utcNow)
    {
        var utc = ToUtc(observedAt);
        if (utc > utcNow.AddSeconds(5))
            throw new NetworkAdminException("INVALID_OBSERVED_AT", 400, "observedAt cannot be in the future.");
        return utc;
    }

    /// <summary>AC-X18 / BR-055: 17,999.9 &lt; 18,000 =&gt; AVAILABLE; 18,000.0 or 18,000.1 =&gt; FULL; missing =&gt; NO_DATA/NO_LIMIT.</summary>
    public static string ComputeLoadStatus(decimal? observedLoadKg, decimal? maxLoadKg) =>
        observedLoadKg is null ? "NO_DATA"
        : maxLoadKg is null ? "NO_LIMIT"
        : observedLoadKg.Value >= maxLoadKg.Value ? "FULL"
        : "AVAILABLE";

    public static (DateTime? ArrivalUtc, DateTime? DepartureUtc) ValidateManualEta(DateTime? arrival, DateTime? departure)
    {
        if (arrival is null && departure is null)
            throw new NetworkAdminException("INVALID_TRIP_ETA", 400, "At least one of expectedArrivalAt or expectedDepartureAt is required.");
        var arrUtc = arrival.HasValue ? ToUtc(arrival.Value) : (DateTime?)null;
        var depUtc = departure.HasValue ? ToUtc(departure.Value) : (DateTime?)null;
        if (arrUtc.HasValue && depUtc.HasValue && depUtc.Value < arrUtc.Value)
            throw new NetworkAdminException("INVALID_TRIP_ETA", 400, "expectedDepartureAt cannot be earlier than expectedArrivalAt.");
        return (arrUtc, depUtc);
    }

    public static void ValidateNoticePublishable(DateTime validFromUtc, DateTime? validToUtc, DateTime utcNow)
    {
        if (validToUtc.HasValue && validToUtc.Value <= utcNow)
            throw new NetworkAdminException("NOTICE_EXPIRED", 422, "Cannot publish a service notice whose validTo timestamp has already passed (UC31).");
        if (validToUtc.HasValue && validToUtc.Value <= validFromUtc)
            throw new NetworkAdminException("INVALID_EFFECTIVE_WINDOW", 400, "validTo must be strictly after validFrom.");
    }

    public static void EnsureNoticeMakerChecker(bool emergencyTemplate, string createdByUserId, string actorUserId)
    {
        if (!emergencyTemplate) EnsureMakerChecker(createdByUserId, actorUserId, "service notice");
    }

    public static DateTime ToUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };

    private static string NormalizeAllowed(string? value, HashSet<string> allowed, string code, string message)
    {
        var normalized = value?.Trim().ToUpperInvariant() ?? string.Empty;
        if (!allowed.Contains(normalized)) throw new NetworkAdminException(code, 400, message);
        return normalized;
    }
}
