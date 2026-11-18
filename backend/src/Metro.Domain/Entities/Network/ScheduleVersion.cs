using Metro.Domain.Common;
using Metro.Domain.Entities.Identity;

namespace Metro.Domain.Entities.Network;

public class ScheduleVersion : IOperatorScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OperatorId { get; set; }
    public Guid LineId { get; set; }
    public Guid ServiceCalendarId { get; set; }
    public int Version { get; set; } = 1;
    public string Code { get; set; } = string.Empty;
    public string Status { get; set; } = "DRAFT"; // DRAFT, PENDING_APPROVAL, APPROVED, PUBLISHED, SUPERSEDED, EXPIRED, REJECTED, CANCELLED
    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public string CreatedByUserId { get; set; } = string.Empty;
    public string? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? PublishedByUserId { get; set; }
    public DateTime? PublishedAt { get; set; }
    public string? DecisionReason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Operator Operator { get; set; } = null!;
    public Line Line { get; set; } = null!;
    public ServiceCalendar ServiceCalendar { get; set; } = null!;
    public ICollection<PlannedTrip> PlannedTrips { get; set; } = new List<PlannedTrip>();

    public TimeWindow GetEffectiveWindow() => new(EffectiveFrom, EffectiveTo);

    public bool IsEffectiveAt(DateTime timestampUtc) =>
        Status == "PUBLISHED" && GetEffectiveWindow().Contains(timestampUtc);
}
