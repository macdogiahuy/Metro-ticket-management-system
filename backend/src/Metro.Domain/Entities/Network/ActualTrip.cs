using Metro.Domain.Common;

namespace Metro.Domain.Entities.Network;

public class ActualTrip : IOperatorScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OperatorId { get; set; }
    public Guid PlannedTripId { get; set; }
    public string Status { get; set; } = "READY"; // READY, IN_SERVICE, COMPLETED, DELAYED, CANCELLED, DISRUPTED
    public DateTime? ActualStartAt { get; set; }
    public DateTime? ActualEndAt { get; set; }
    public string Source { get; set; } = "MANUAL"; // MANUAL, OCC_IMPORT, SIMULATION
    public string? SourceReference { get; set; }
    public int? DelaySeconds { get; set; }
    public string? Reason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public PlannedTrip PlannedTrip { get; set; } = null!;
    public ICollection<ActualStopTime> StopTimes { get; set; } = new List<ActualStopTime>();
}
