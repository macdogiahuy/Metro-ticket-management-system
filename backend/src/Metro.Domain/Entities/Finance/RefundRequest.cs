using Metro.Domain.Common;

namespace Metro.Domain.Entities.Finance;

public sealed class RefundRequest : IOperatorScoped
{
    public Guid Id { get; set; }
    public Guid OperatorId { get; set; }
    public string RequestCode { get; set; } = null!;
    public Guid TicketId { get; set; }
    public Guid PaymentId { get; set; }
    public Guid? IncidentId { get; set; }
    public string ReasonType { get; set; } = null!;
    public long RequestedAmountMinor { get; set; }
    public long? ApprovedAmountMinor { get; set; }
    public RefundRequestStatus Status { get; set; } = RefundRequestStatus.Submitted;
    public string? SubmittedByUserId { get; set; }
    public string? ReviewedByUserId { get; set; }
    public string? ApprovedByUserId { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public string? DecisionReason { get; set; }
    public Guid CorrelationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Payment Payment { get; set; } = null!;
}

public enum RefundRequestStatus
{
    Submitted,
    UnderReview,
    NeedsInfo,
    PendingApproval,
    Approved,
    Processing,
    Completed,
    Failed,
    Rejected,
    Cancelled
}
