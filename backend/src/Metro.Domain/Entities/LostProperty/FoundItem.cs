using Metro.Domain.Common;

namespace Metro.Domain.Entities.LostProperty;

public sealed class FoundItem : IOperatorScoped
{
    public Guid Id { get; set; }
    public Guid OperatorId { get; set; }
    public string ItemCode { get; set; } = null!;
    public string ReceivedByUserId { get; set; } = null!;
    public DateTimeOffset ReceivedAt { get; set; }
    public Guid HoldingStationId { get; set; }
    public DateTimeOffset? FoundAt { get; set; }
    public Guid? PlannedTripId { get; set; }
    public Guid? CarId { get; set; }
    public string PublicDescription { get; set; } = null!;
    public string? SecretCharacteristics { get; set; }
    public string? AttachmentReference { get; set; }
    public FoundItemStatus Status { get; set; } = FoundItemStatus.Held;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public enum FoundItemStatus
{
    Held,
    ClaimPending,
    Released,
    Overdue,
    Disposed
}
