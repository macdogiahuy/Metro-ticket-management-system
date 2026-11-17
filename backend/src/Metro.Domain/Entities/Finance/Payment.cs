using Metro.Domain.Common;

namespace Metro.Domain.Entities.Finance;

public sealed class Payment : IOperatorScoped
{
    public Guid Id { get; set; }
    public Guid OperatorId { get; set; }
    public Guid OrderId { get; set; }
    public string PaymentCode { get; set; } = null!;
    public string Provider { get; set; } = null!;
    public string? ProviderPaymentReference { get; set; }
    public PaymentMethod Method { get; set; }
    public long AmountMinor { get; set; }
    public string Currency { get; set; } = "VND";
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public string IdempotencyKey { get; set; } = null!;
    public DateTimeOffset InitiatedAt { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; }
    public DateTimeOffset? SucceededAt { get; set; }
    public string? FailureCode { get; set; }
    public PaymentReconciliationStatus ReconciliationStatus { get; set; } = PaymentReconciliationStatus.Unreconciled;
    public Guid CorrelationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public enum PaymentMethod
{
    SandboxGateway,
    Cash,
    BankTransfer,
    Other
}

public enum PaymentStatus
{
    Pending,
    Succeeded,
    Failed,
    Expired,
    Cancelled,
    PartiallyRefunded,
    Refunded,
    ReviewRequired
}

public enum PaymentReconciliationStatus
{
    Unreconciled,
    Matched,
    Mismatched,
    ManualReview
}
