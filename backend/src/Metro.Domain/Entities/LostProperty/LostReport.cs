using Metro.Domain.Common;

namespace Metro.Domain.Entities.LostProperty;

public sealed class LostReport : IOperatorScoped
{
    public Guid Id { get; set; }
    public Guid OperatorId { get; set; }
    public string ReportCode { get; set; } = null!;
    public string SubmittedByUserId { get; set; } = null!;
    public string OwnerUserId { get; set; } = null!;
    public string ItemCategory { get; set; } = null!;
    public string? Color { get; set; }
    public string PublicDescription { get; set; } = null!;
    public string? SecretCharacteristics { get; set; }
    public DateTimeOffset LostFrom { get; set; }
    public DateTimeOffset? LostTo { get; set; }
    public Guid? StationId { get; set; }
    public Guid? LineId { get; set; }
    public Guid? PlannedTripId { get; set; }
    public Guid? CarId { get; set; }
    public string? AttachmentReference { get; set; }
    public LostReportStatus Status { get; set; } = LostReportStatus.Open;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public enum LostReportStatus
{
    Open,
    Matched,
    ClaimPending,
    Resolved,
    Withdrawn,
    Overdue,
    Closed
}
