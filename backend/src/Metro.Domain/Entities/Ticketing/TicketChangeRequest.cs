using Metro.Domain.Common;

namespace Metro.Domain.Entities.Ticketing;

public sealed class TicketChangeRequest : IOperatorScoped
{
    public Guid Id { get; set; }
    public Guid OperatorId { get; set; }
    public string RequestCode { get; set; } = null!;
    public Guid TicketId { get; set; }
    public Guid OrderId { get; set; }
    public string RequestedByUserId { get; set; } = null!;
    public TicketChangeReasonType ReasonType { get; set; }
    public Guid? IncidentId { get; set; }
    public DateOnly? OriginalServiceDate { get; set; }
    public DateOnly? RequestedServiceDate { get; set; }
    public Guid? OriginalOriginStationId { get; set; }
    public Guid? OriginalDestinationStationId { get; set; }
    public Guid? NewOriginStationId { get; set; }
    public Guid? NewDestinationStationId { get; set; }
    public Guid? OriginalPlannedTripId { get; set; }
    public Guid? NewPlannedTripId { get; set; }
    public TicketChangeRequestStatus Status { get; set; } = TicketChangeRequestStatus.Requested;
    public string? DecidedByUserId { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecisionReason { get; set; }
    public DateTimeOffset? AppliedAt { get; set; }
    public short ChangeSequence { get; set; }
    public Guid? RelatedChildTicketId { get; set; }
    public Guid CorrelationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public enum TicketChangeReasonType
{
    ServiceDisruption,
    SystemError,
    Other
}

public enum TicketChangeRequestStatus
{
    Requested,
    Approved,
    Rejected,
    Applied,
    Cancelled,
    Expired
}
