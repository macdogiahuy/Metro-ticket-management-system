using Metro.Domain.Common;

namespace Metro.Domain.Entities.Ticketing;

public sealed class PurchaseConsent : IOperatorScoped
{
    public Guid Id { get; set; }
    public Guid OperatorId { get; set; }
    public Guid OrderId { get; set; }
    public int OrderVersion { get; set; }
    public string BuyerUserId { get; set; } = null!;
    public string BeneficiaryUserId { get; set; } = null!;
    public PurchaseConsentStatus Status { get; set; } = PurchaseConsentStatus.Requested;
    public string? OrderContentHash { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RespondedAt { get; set; }
    public string? ResponseNote { get; set; }
    public Guid CorrelationId { get; set; }
}

public enum PurchaseConsentStatus
{
    Requested,
    Accepted,
    Declined,
    Withdrawn,
    Expired
}
