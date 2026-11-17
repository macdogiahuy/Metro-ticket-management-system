using Metro.Domain.Common;

namespace Metro.Domain.Entities.Ticketing;

public sealed class Journey : IOperatorScoped
{
    public Guid Id { get; set; }
    public Guid OperatorId { get; set; }
    public string JourneyCode { get; set; } = null!;
    public Guid TicketId { get; set; }
    public Guid? PlannedOriginStationId { get; set; }
    public Guid? PlannedDestinationStationId { get; set; }
    public Guid? ActualEntryStationId { get; set; }
    public Guid? ActualExitStationId { get; set; }
    public JourneyStatus Status { get; set; } = JourneyStatus.Open;
    public DateTimeOffset? EnteredAt { get; set; }
    public DateTimeOffset? ExitedAt { get; set; }
    public long SupplementalFareMinor { get; set; }
    public Guid CorrelationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateOnly? ServiceDate { get; set; }
    public Guid? EntryPlannedTripId { get; set; }
    public Guid? ExitPlannedTripId { get; set; }
    public short? EntryDirection { get; set; }
    public Guid? PairedJourneyId { get; set; }

    public Journey? PairedJourney { get; set; }
}

public enum JourneyStatus
{
    Open,
    Completed,
    ManualReview,
    Cancelled
}
