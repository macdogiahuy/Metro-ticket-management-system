using Metro.Domain.Common;
using Metro.Domain.Entities.Identity;

namespace Metro.Domain.Entities.Orders;

public class FarePolicyVersion : IOperatorScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OperatorId { get; set; }
    public string Code { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
    public string ScopeKey { get; set; } = string.Empty;
    public string Status { get; set; } = "DRAFT"; // DRAFT, PENDING_APPROVAL, APPROVED, PUBLISHED, SUPERSEDED, EXPIRED, REJECTED, CANCELLED
    public string Currency { get; set; } = "VND";
    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public string CreatedByUserId { get; set; } = string.Empty;
    public string? ApprovedByUserId { get; set; }
    public string? PublishedByUserId { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime? PublishedAt { get; set; }
    public string? DecisionReason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Operator Operator { get; set; } = null!;
    public ICollection<Order> Orders { get; set; } = new List<Order>();
    public ICollection<OrderVersion> OrderVersions { get; set; } = new List<OrderVersion>();

    public TimeWindow GetEffectiveWindow() => new(EffectiveFrom, EffectiveTo);

    public bool IsEffectiveAt(DateTime timestampUtc) =>
        Status == "PUBLISHED" && GetEffectiveWindow().Contains(timestampUtc);
}
