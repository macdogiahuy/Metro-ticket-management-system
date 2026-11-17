using Metro.Domain.Common;
using Metro.Domain.Entities.Identity;

namespace Metro.Domain.Entities.Network;

public class Line : IOperatorScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OperatorId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = "ACTIVE"; // PLANNED, ACTIVE, SUSPENDED, CLOSED
    public DateOnly? OpenedOn { get; set; }
    public string? CreatedByUserId { get; set; }
    public string? UpdatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Operator Operator { get; set; } = null!;
    public ICollection<LineStation> LineStations { get; set; } = new List<LineStation>();
    public ICollection<ScheduleVersion> ScheduleVersions { get; set; } = new List<ScheduleVersion>();
    public ICollection<PlannedTrip> PlannedTrips { get; set; } = new List<PlannedTrip>();
}
