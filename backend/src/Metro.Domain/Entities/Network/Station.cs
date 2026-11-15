using Metro.Domain.Common;
using Metro.Domain.Entities.Identity;

namespace Metro.Domain.Entities.Network;

public class Station : IOperatorScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OperatorId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string StationType { get; set; } = "ELEVATED"; // AT_GRADE, ELEVATED, UNDERGROUND, INTERCHANGE
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public string Status { get; set; } = "ACTIVE"; // PLANNED, ACTIVE, SUSPENDED, CLOSED
    public string? CreatedByUserId { get; set; }
    public string? UpdatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Operator Operator { get; set; } = null!;
    public ICollection<LineStation> LineStations { get; set; } = new List<LineStation>();
    public ICollection<PlannedStopTime> PlannedStopTimes { get; set; } = new List<PlannedStopTime>();
}
