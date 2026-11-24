using Metro.Application.Abstractions.Operations;
using Metro.Application.Ticketing;

namespace Metro.Application.Operations;

public static class OperationsRules
{
    public static string RequireReason(string? reason)
    {
        var value = reason?.Trim();
        if (string.IsNullOrEmpty(value) || value.Length > 1000) throw new OperationsException("OPERATIONS_REASON_REQUIRED", 400);
        return value;
    }

    public static void ValidateKey(string? key)
    {
        if (key is null || key.Length is < 8 or > 120 || key.Any(c => c is < '!' or > '~'))
            throw new OperationsException("OPERATIONS_IDEMPOTENCY_KEY_REQUIRED", 400);
    }

    public static void ValidateEta(UpdateEtaRequest request, DateOnly serviceDate)
    {
        RequireReason(request.Reason);
        if (request.StationId == Guid.Empty || (request.ExpectedArrivalAt is null && request.ExpectedDepartureAt is null) ||
            (request.ExpectedArrivalAt is { } a && request.ExpectedDepartureAt is { } d && d < a))
            throw new OperationsException("INVALID_TRIP_ETA", 400);
        foreach (var stamp in new[] { request.ExpectedArrivalAt, request.ExpectedDepartureAt })
            if (stamp is { } value && TicketRules.LocalDate(value) != serviceDate)
                throw new OperationsException("ETA_OUTSIDE_SERVICE_DAY", 422);
    }

    public static void ValidateLoad(RecordLoadRequest request, DateTimeOffset now, DateOnly serviceDate)
    {
        RequireReason(request.Reason);
        if (request.ObservedLoadKg < 0 || request.ObservedLoadKg > 9_999_999.9m ||
            decimal.Round(request.ObservedLoadKg, 1) != request.ObservedLoadKg)
            throw new OperationsException("INVALID_LOAD_KG", 400);
        if (request.ObservedAt > now || TicketRules.LocalDate(request.ObservedAt) != serviceDate)
            throw new OperationsException("INVALID_LOAD_OBSERVED_AT", 422);
    }

    public static string ValidateTripTransition(string current, string? requested)
    {
        var target = requested?.Trim().ToUpperInvariant() ?? "";
        if (target is not ("PLANNED" or "READY" or "IN_SERVICE" or "DELAYED" or "DISRUPTED" or "COMPLETED" or "CANCELLED"))
            throw new OperationsException("INVALID_TRIP_STATUS", 400);
        if (current is "COMPLETED" or "CANCELLED" && target != current)
            throw new OperationsException("TRIP_ALREADY_ENDED", 409);
        if (target == "PLANNED" && current != "PLANNED")
            throw new OperationsException("INVALID_TRIP_TRANSITION", 409);
        if (target == "READY" && current is not ("PLANNED" or "READY"))
            throw new OperationsException("INVALID_TRIP_TRANSITION", 409);
        return target;
    }

    public static string ValidateDisruption(CreateDisruptionRequest request, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 200 ||
            string.IsNullOrWhiteSpace(request.Description) || request.Description.Length > 4000 ||
            request.OccurredAt > now || request.OccurredAt == default ||
            new[] { request.LineId, request.StationId, request.PlannedTripId }.Count(id => id.HasValue) != 1)
            throw new OperationsException("INVALID_DISRUPTION", 400);
        var effect = request.Effect?.Trim().ToUpperInvariant() ?? "";
        if (request.StationId.HasValue ? effect != "CLOSED" : effect is not ("DELAYED" or "SUSPENDED"))
            throw new OperationsException("INVALID_DISRUPTION_EFFECT", 400);
        return effect;
    }
}
