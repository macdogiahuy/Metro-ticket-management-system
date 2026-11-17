using Metro.Domain.Common;

namespace Metro.Domain.Entities.Identity;

public class AuthSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = string.Empty;
    public string RefreshTokenHash { get; set; } = string.Empty;
    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastActiveAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? RevocationReason { get; set; }
    public string? DeviceSummary { get; set; }

    public TimeWindow GetValidityWindow() => new(IssuedAt, ExpiresAt);

    public bool IsActiveAt(DateTime timestampUtc) =>
        RevokedAt == null && GetValidityWindow().Contains(timestampUtc);
}
