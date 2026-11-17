using Metro.Domain.Common;

namespace Metro.Domain.Entities.Finance;

public sealed class PaymentEvent : IOperatorScoped
{
    public Guid Id { get; set; }
    public Guid OperatorId { get; set; }
    public Guid PaymentId { get; set; }
    public string ProviderEventId { get; set; } = null!;
    public string IdempotencyKey { get; set; } = null!;
    public string EventType { get; set; } = null!;
    public bool SignatureVerified { get; set; }
    public long? ReceivedAmountMinor { get; set; }
    public string? ReceivedCurrency { get; set; }
    public string PayloadHash { get; set; } = null!;
    public string Outcome { get; set; } = null!;
    public DateTimeOffset ReceivedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public Guid CorrelationId { get; set; }

    public Payment Payment { get; set; } = null!;
}
