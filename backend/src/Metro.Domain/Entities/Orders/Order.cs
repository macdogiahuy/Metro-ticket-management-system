using Metro.Domain.Common;
using Metro.Domain.Entities.Identity;
using Metro.Domain.Entities.Network;

namespace Metro.Domain.Entities.Orders;

public class Order : IOperatorScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OperatorId { get; set; }
    public string OrderCode { get; set; } = string.Empty;
    public string Channel { get; set; } = "WEB"; // WEB, COUNTER, ADMIN
    public string? PayerUserId { get; set; }
    public string? BeneficiaryUserId { get; set; }
    public Guid? SellingStationId { get; set; }
    public string? CreatedByUserId { get; set; }
    public string? PayerNameSnapshot { get; set; }
    public string? PayerEmailSnapshot { get; set; }
    public string? BeneficiaryNameSnapshot { get; set; }
    public string Status { get; set; } = "DRAFT"; // DRAFT, HELD, PENDING_PAYMENT, PAID, ISSUED, EXPIRED, CANCELLED, REFUND_PENDING, REFUNDED
    public string Currency { get; set; } = "VND";
    public long SubtotalMinor { get; set; }
    public long DiscountMinor { get; set; }
    public long TotalMinor { get; set; }
    public Guid FarePolicyVersionId { get; set; }
    public string FareSnapshot { get; set; } = "{}";
    public DateTime? HoldExpiresAt { get; set; }
    public DateTime? PaymentStartedAt { get; set; }
    public DateTime? PaidAt { get; set; }
    public DateTime? IssuedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancelledByUserId { get; set; }
    public string? CancellationReason { get; set; }
    public Guid? ConsentId { get; set; }
    public Guid CorrelationId { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Operator Operator { get; set; } = null!;
    public Station? SellingStation { get; set; }
    public FarePolicyVersion FarePolicyVersion { get; set; } = null!;
    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
    public ICollection<OrderVersion> Versions { get; set; } = new List<OrderVersion>();

    public bool IsHoldExpired(DateTime timestampUtc)
    {
        if (!HoldExpiresAt.HasValue) return false;
        var utc = timestampUtc.Kind == DateTimeKind.Utc ? timestampUtc : timestampUtc.ToUniversalTime();
        // EndExclusive: when timestampUtc >= HoldExpiresAt, hold has expired.
        return utc >= HoldExpiresAt.Value;
    }
}
