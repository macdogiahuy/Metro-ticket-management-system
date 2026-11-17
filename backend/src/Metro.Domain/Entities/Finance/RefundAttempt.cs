using Metro.Domain.Common;

namespace Metro.Domain.Entities.Finance;

public sealed class RefundAttempt : IOperatorScoped
{
    public Guid Id { get; set; }
    public Guid OperatorId { get; set; }
    public Guid RefundTransactionId { get; set; }
    public int AttemptNo { get; set; }
    public string? ProviderEventId { get; set; }
    public string RequestHash { get; set; } = null!;
    public string? ResponseHash { get; set; }
    public RefundAttemptStatus Status { get; set; }
    public string? ErrorCode { get; set; }
    public DateTimeOffset AttemptedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public RefundTransaction RefundTransaction { get; set; } = null!;
}

public enum RefundAttemptStatus
{
    Sent,
    Succeeded,
    Failed,
    Timeout,
    Unknown
}
