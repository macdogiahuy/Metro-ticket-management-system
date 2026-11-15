using Metro.Domain.Common;

namespace Metro.Domain.Entities.Network;

public class PlannedTrip : IOperatorScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OperatorId { get; set; }
    public Guid ScheduleVersionId { get; set; }
    public Guid LineId { get; set; }
    public string TripCode { get; set; } = string.Empty;
    public DateOnly ServiceDate { get; set; }
    public short Direction { get; set; } // 0 or 1
    public DateTime PlannedStartAt { get; set; }
    public DateTime PlannedEndAt { get; set; }
    public string Status { get; set; } = "PLANNED"; // PLANNED, READY, IN_SERVICE, COMPLETED, DELAYED, CANCELLED, DISRUPTED
    public string? CancellationReason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ScheduleVersion ScheduleVersion { get; set; } = null!;
    public Line Line { get; set; } = null!;
    public ICollection<PlannedStopTime> StopTimes { get; set; } = new List<PlannedStopTime>();
    public ActualTrip? ActualTrip { get; set; }

    public TimeWindow GetPlannedWindow() => new(PlannedStartAt, PlannedEndAt);
}
