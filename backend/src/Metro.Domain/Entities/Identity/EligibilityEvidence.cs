namespace Metro.Domain.Entities.Identity;

/// <summary>A passenger's time-bounded request for a concession category.</summary>
public sealed class EligibilityEvidence
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Status { get; set; } = "PENDING";
    public DateOnly? ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
    public string? EvidenceObjectKey { get; set; }
    public string? EvidenceFileName { get; set; }
    public string? EvidenceContentType { get; set; }
    public long? EvidenceSize { get; set; }
    public string? EvidenceSha256 { get; set; }
    public string? DecisionReason { get; set; }
    public string? DecidedByUserId { get; set; }
    public DateTime? DecidedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
