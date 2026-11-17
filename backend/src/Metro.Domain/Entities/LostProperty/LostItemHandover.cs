using Metro.Domain.Common;

namespace Metro.Domain.Entities.LostProperty;

public sealed class LostItemHandover : IOperatorScoped
{
    public Guid Id { get; set; }
    public Guid OperatorId { get; set; }
    public Guid FoundItemId { get; set; }
    public Guid ClaimId { get; set; }
    public string HandedToUserId { get; set; } = null!;
    public string HandedByUserId { get; set; } = null!;
    public DateTimeOffset HandedAt { get; set; }
    public string? EvidenceReference { get; set; }
    public string? ConfirmationNote { get; set; }

    public FoundItem FoundItem { get; set; } = null!;
    public LostItemClaim Claim { get; set; } = null!;
}
