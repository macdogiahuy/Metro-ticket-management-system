using Metro.Domain.Common;

namespace Metro.Domain.Entities.Passengers;

public sealed class GuardianLink : IOperatorScoped
{
    public Guid Id { get; set; }
    public Guid OperatorId { get; set; }
    public string GuardianUserId { get; set; } = null!;
    public string ChildUserId { get; set; } = null!;
    public GuardianRelationship Relationship { get; set; }
    public GuardianVerificationType VerificationType { get; set; } = GuardianVerificationType.Guardianship;
    public GuardianLinkStatus Status { get; set; } = GuardianLinkStatus.Pending;
    public string? EvidenceReference { get; set; }
    public DateTimeOffset? EvidencePurgedAt { get; set; }
    public DateTimeOffset SubmittedAt { get; set; }
    public string? VerifiedByUserId { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; }
    public DateOnly? ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
    public string? DecisionReason { get; set; }
    public string? RevocationReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public enum GuardianRelationship
{
    Parent,
    LegalGuardian,
    Other
}

public enum GuardianVerificationType
{
    Guardianship,
    ElderlyAge
}

public enum GuardianLinkStatus
{
    Pending,
    Active,
    Revoked,
    Expired
}
