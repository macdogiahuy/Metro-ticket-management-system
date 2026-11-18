using Metro.Domain.Entities.Tickets;
using Metro.Domain.Common;

namespace Metro.Domain.Entities.Ticketing;

public sealed class AfcEvent : IOperatorScoped
{
    public Guid Id { get; set; }
    public Guid OperatorId { get; set; }
    public string EventCode { get; set; } = null!;
    public string IdempotencyKey { get; set; } = null!;
    public Guid? JourneyId { get; set; }
    public Guid TicketId { get; set; }
    public Guid? CredentialId { get; set; }
    public AfcEventType EventType { get; set; }
    public Guid StationId { get; set; }
    public string DeviceCode { get; set; } = null!;
    public DateTimeOffset EventAt { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public bool Offline { get; set; }
    public AfcEventDecision Decision { get; set; }
    public string ReasonCode { get; set; } = null!;
    public string? ActorUserId { get; set; }
    public string? ManualReason { get; set; }
    public Guid CorrelationId { get; set; }
    public Guid? PlannedTripId { get; set; }
    public short? Direction { get; set; }

    public Journey? Journey { get; set; }
    public TicketCredential? Credential { get; set; }
}

public enum AfcEventType
{
    TapIn,
    TapOut,
    ManualEntry,
    ManualExit,
    SyncOffline
}

public enum AfcEventDecision
{
    Allow,
    Deny,
    Review
}
