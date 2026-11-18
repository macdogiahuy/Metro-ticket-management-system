using Metro.Domain.Common;

namespace Metro.Domain.Entities.LostProperty;

public sealed class LostItemClaim : IOperatorScoped
{
    public Guid Id { get; set; }
    public Guid OperatorId { get; set; }
    public string ClaimCode { get; set; } = null!;
    public Guid? LostReportId { get; set; }
    public Guid FoundItemId { get; set; }
    public string ClaimantUserId { get; set; } = null!;
    public string? EvidenceReference { get; set; }
    public LostItemClaimStatus Status { get; set; } = LostItemClaimStatus.Submitted;
    public string? DecidedByUserId { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecisionReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public LostReport? LostReport { get; set; }
    public FoundItem FoundItem { get; set; } = null!;
}

public enum LostItemClaimStatus
{
    Submitted,
    Approved,
    Rejected,
    Withdrawn,
    Expired
}
