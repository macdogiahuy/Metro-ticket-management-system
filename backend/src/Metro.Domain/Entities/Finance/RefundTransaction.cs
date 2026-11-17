using Metro.Domain.Common;

namespace Metro.Domain.Entities.Finance;

public sealed class RefundTransaction : IOperatorScoped
{
    public Guid Id { get; set; }
    public Guid OperatorId { get; set; }
    public Guid RefundRequestId { get; set; }
    public Guid PaymentId { get; set; }
    public string RefundCode { get; set; } = null!;
    public string Provider { get; set; } = null!;
    public string? ProviderRefundReference { get; set; }
    public string Method { get; set; } = null!;
    public long AmountMinor { get; set; }
    public string Currency { get; set; } = "VND";
    public RefundTransactionStatus Status { get; set; } = RefundTransactionStatus.Pending;
    public string IdempotencyKey { get; set; } = null!;
    public string PayoutRecipientSnapshot { get; set; } = "{}";
    public string? ProcessedByUserId { get; set; }
    public DateTimeOffset? SucceededAt { get; set; }
    public Guid CorrelationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public RefundRequest RefundRequest { get; set; } = null!;
    public Payment Payment { get; set; } = null!;
}

public enum RefundTransactionStatus
{
    Pending,
    Processing,
    Succeeded,
    Failed,
    Unknown,
    Cancelled
}
