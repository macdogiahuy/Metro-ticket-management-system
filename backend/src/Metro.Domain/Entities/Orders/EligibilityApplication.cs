using Metro.Domain.Common;

namespace Metro.Domain.Entities.Orders;

public class EligibilityApplication : IOperatorScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OperatorId { get; set; }
    public string ApplicationCode { get; set; } = string.Empty;
    public string ApplicantUserId { get; set; } = string.Empty;
    public Guid EligibilityPolicyId { get; set; }
    public string Status { get; set; } = "DRAFT"; // DRAFT, SUBMITTED, NEEDS_INFO, APPROVED, REJECTED, EXPIRED, REVOKED
    public DateTime? SubmittedAt { get; set; }
    public string? ReviewedByUserId { get; set; }
    public DateTime? DecidedAt { get; set; }
    public DateOnly? ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
    public string? DecisionReason { get; set; }
    public DateTime? EvidencePurgedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public EligibilityPolicy EligibilityPolicy { get; set; } = null!;
}
