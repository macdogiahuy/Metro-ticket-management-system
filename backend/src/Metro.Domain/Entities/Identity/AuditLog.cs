using Metro.Domain.Common;

namespace Metro.Domain.Entities.Identity;

public class AuditLog : IOptionalOperatorScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? OperatorId { get; set; }
    public string? ActorUserId { get; set; }
    public string DatabaseActor { get; set; } = "app";
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string[] ChangedFields { get; set; } = Array.Empty<string>();
    public string? BeforeHash { get; set; }
    public string? AfterHash { get; set; }
    public string? Reason { get; set; }
    public string Outcome { get; set; } = "SUCCEEDED";
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public Guid CorrelationId { get; set; } = Guid.NewGuid();

    public Operator? Operator { get; set; }
}
