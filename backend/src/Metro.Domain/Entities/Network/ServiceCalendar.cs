using Metro.Domain.Common;
using Metro.Domain.Entities.Identity;

namespace Metro.Domain.Entities.Network;

public class ServiceCalendar : IOperatorScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OperatorId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DayType { get; set; } = "WEEKDAY"; // WEEKDAY, WEEKEND, HOLIDAY, EVENT, DISRUPTION
    public short[] DaysOfWeek { get; set; } = Array.Empty<short>();
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public short Priority { get; set; }
    public string Status { get; set; } = "DRAFT"; // DRAFT, PUBLISHED, SUPERSEDED, EXPIRED, CANCELLED
    public string? CreatedByUserId { get; set; }
    public string? ApprovedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Operator Operator { get; set; } = null!;
    public ICollection<ScheduleVersion> ScheduleVersions { get; set; } = new List<ScheduleVersion>();

    public DateWindow GetEffectiveWindow() => new(EffectiveFrom, EffectiveTo);

    public bool IsEffectiveOn(DateOnly date) =>
        Status == "PUBLISHED" && GetEffectiveWindow().Contains(date);
}
