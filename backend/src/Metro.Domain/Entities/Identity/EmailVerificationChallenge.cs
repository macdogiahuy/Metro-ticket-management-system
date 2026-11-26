using Metro.Domain.Common;

namespace Metro.Domain.Entities.Identity;

public class EmailVerificationChallenge
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = string.Empty;
    public string Purpose { get; set; } = "VERIFY_EMAIL"; // VERIFY_EMAIL, RESET_PASSWORD, TICKET_QR_SUPPORT; MFA retained for legacy data.
    public string CodeHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public short FailedAttempts { get; set; } = 0;
    public DateTime? ConsumedAt { get; set; }
    public DateTime? SupersededAt { get; set; }
    public string DeliveryStatus { get; set; } = "PENDING"; // PENDING, SENT, FAILED

    public TimeWindow GetValidityWindow() => new(CreatedAt, ExpiresAt);

    public bool IsUsableAt(DateTime timestampUtc) =>
        ConsumedAt == null && SupersededAt == null && FailedAttempts < 5 && GetValidityWindow().Contains(timestampUtc);
}
