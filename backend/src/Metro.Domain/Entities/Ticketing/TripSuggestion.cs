using Metro.Domain.Common;

namespace Metro.Domain.Entities.Ticketing;

public sealed class TripSuggestion : IOperatorScoped
{
    public Guid Id { get; set; }
    public Guid OperatorId { get; set; }
    public Guid OrderId { get; set; }
    public int OrderVersion { get; set; }
    public string SuggestionCode { get; set; } = null!;
    public string BeneficiaryUserId { get; set; } = null!;
    public DateOnly ServiceDate { get; set; }
    public Guid OriginStationId { get; set; }
    public Guid DestinationStationId { get; set; }
    public Guid? LineId { get; set; }
    public short? Direction { get; set; }
    public Guid? SuggestedPlannedTripId { get; set; }
    public DateTimeOffset? SuggestedDepartureAt { get; set; }
    public TripSuggestionStatus Status { get; set; } = TripSuggestionStatus.Suggested;
    public string? NoTripReason { get; set; }
    public TripSuggestionSource Source { get; set; } = TripSuggestionSource.Schedule;
    public Guid? ScheduleVersionId { get; set; }
    public string? SourceVersionLabel { get; set; }
    public int? DataFreshnessSeconds { get; set; }
    public string? UpdateReason { get; set; }
    public DateTimeOffset ComputedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? SupersededAt { get; set; }
    public Guid CorrelationId { get; set; }
}

public enum TripSuggestionStatus
{
    Suggested,
    NoTrip,
    Superseded,
    Failed
}

public enum TripSuggestionSource
{
    Schedule,
    Eta,
    Manual
}
