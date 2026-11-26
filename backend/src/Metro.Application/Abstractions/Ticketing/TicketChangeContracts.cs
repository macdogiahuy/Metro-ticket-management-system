namespace Metro.Application.Abstractions.Ticketing;

public sealed record CreateTicketChangeRequest(
    DateOnly RequestedServiceDate,
    Guid? NewOriginStationId = null,
    Guid? NewDestinationStationId = null,
    Guid? NewPlannedTripId = null,
    string ReasonType = "SERVICE_DISRUPTION",
    Guid? IncidentId = null,
    string? DecisionReason = null
);

public sealed record ApproveTicketChangeRequest(
    string? DecisionReason = null
);

public sealed record TicketChangeResponse(
    Guid Id,
    Guid OperatorId,
    string RequestCode,
    Guid TicketId,
    Guid OrderId,
    string RequestedByUserId,
    string ReasonType,
    Guid? IncidentId,
    DateOnly? OriginalServiceDate,
    DateOnly? RequestedServiceDate,
    Guid? OriginalOriginStationId,
    Guid? OriginalDestinationStationId,
    Guid? NewOriginStationId,
    Guid? NewDestinationStationId,
    Guid? OriginalPlannedTripId,
    Guid? NewPlannedTripId,
    string Status,
    string? DecidedByUserId,
    DateTimeOffset? DecidedAt,
    string? DecisionReason,
    DateTimeOffset? AppliedAt,
    short ChangeSequence,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

public sealed class TicketChangeException(string code, int status, string message) : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
}

public interface ITicketChangeService
{
    Task<TicketChangeResponse> CreateAsync(Guid ticketId, CreateTicketChangeRequest request, string userId, string? operatorCode, string idempotencyKey, CancellationToken ct);
    Task<TicketChangeResponse> ApproveAsync(Guid changeRequestId, ApproveTicketChangeRequest request, string staffUserId, string? operatorCode, string idempotencyKey, CancellationToken ct);
    Task<TicketChangeResponse> GetAsync(Guid changeRequestId, string userId, string? operatorCode, CancellationToken ct);
}
