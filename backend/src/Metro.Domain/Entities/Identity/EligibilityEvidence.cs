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
    public bool EvidenceHold { get; set; }
    public List<EligibilityAttachment> Attachments { get; set; } = [];
    public List<EligibilityDecision> Decisions { get; set; } = [];
}

public sealed class EligibilityAttachment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RequestId { get; set; }
    public int Slot { get; set; }
    public string? ObjectKey { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long Size { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    public DateTime? PurgedAt { get; set; }
    public DateTime? ReplacedAt { get; set; }
}

public sealed class EligibilityDecision
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RequestId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string? ActorUserId { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public DateOnly? ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
}
