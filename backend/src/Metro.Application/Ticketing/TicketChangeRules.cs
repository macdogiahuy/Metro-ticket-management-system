using Metro.Application.Abstractions.Ticketing;
using Metro.Domain.Entities.Ticketing;

namespace Metro.Application.Ticketing;

public static class TicketChangeRules
{
    private static readonly HashSet<string> AllowedReasonTypes =
    [
        "SERVICE_DISRUPTION",
        "SYSTEM_ERROR",
        "OTHER"
    ];

    public static void ValidateTicketEligibility(string status, DateTime? firstUsedAt, bool hasOpenJourney, short changeCount)
    {
        if (changeCount >= 1)
            throw new TicketChangeException("TICKET_CHANGE_LIMIT_REACHED", 409, "Ticket change limit reached (maximum 1 change per ticket).");

        var normalizedStatus = status.Trim().ToUpperInvariant();
        if (normalizedStatus is not ("ISSUED" or "ACTIVE") || firstUsedAt is not null || hasOpenJourney)
            throw new TicketChangeException("TICKET_ALREADY_USED", 409, "Only an unused ticket can be changed.");
    }

    public static TicketChangeReasonType NormalizeReasonType(string reasonType)
    {
        var upper = (reasonType ?? string.Empty).Trim().ToUpperInvariant();
        if (!AllowedReasonTypes.Contains(upper))
            throw new TicketChangeException("INVALID_CHANGE_REASON_TYPE", 400, $"Reason type '{reasonType}' is not supported.");

        return upper switch
        {
            "SERVICE_DISRUPTION" => TicketChangeReasonType.ServiceDisruption,
            "SYSTEM_ERROR" => TicketChangeReasonType.SystemError,
            _ => TicketChangeReasonType.Other
        };
    }

    public static void ValidateRequestedServiceDate(DateOnly requestedDate, DateOnly todayUtc7)
    {
        if (requestedDate < todayUtc7)
            throw new TicketChangeException("SERVICE_DATE_IN_PAST", 400, "Requested service date cannot be in the past (UTC+7).");
    }

    public static void ValidateStations(Guid? originStationId, Guid? destinationStationId)
    {
        if (originStationId.HasValue && destinationStationId.HasValue && originStationId.Value == destinationStationId.Value)
            throw new TicketChangeException("SAME_STATION_NOT_ALLOWED", 400, "Origin and destination stations must be different.");
    }

    public static void ValidateApproval(string status, string? decisionReason)
    {
        var upper = (status ?? string.Empty).Trim().ToUpperInvariant();
        if (upper != "REQUESTED")
            throw new TicketChangeException("INVALID_REQUEST_STATUS", 409, $"Ticket change request is '{status}', expected 'REQUESTED'.");

        if (string.IsNullOrWhiteSpace(decisionReason))
            throw new TicketChangeException("DECISION_REASON_REQUIRED", 400, "Decision reason is required when approving a change request.");
    }
}
