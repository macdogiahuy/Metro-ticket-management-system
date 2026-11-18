namespace Metro.Domain.Entities.Identity;

public class UserProfile
{
    public string UserId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string NormalizedFullName { get; set; } = string.Empty;
    public DateOnly BirthDate { get; set; }
    public byte[]? CitizenIdCiphertext { get; set; }
    public string? CitizenIdLookupHash { get; set; }
    public string? CitizenIdLast4 { get; set; }
    public DateTime? CitizenIdVerifiedAt { get; set; }
    public string? BankCode { get; set; }
    public byte[]? BankAccountCiphertext { get; set; }
    public string? BankAccountLast4 { get; set; }
    public string? BankHolderName { get; set; }
    public string Status { get; set; } = "PENDING"; // PENDING, ACTIVE, LOCKED, DISABLED, CLOSED
    public string Locale { get; set; } = "vi-VN";
    public bool EmailNotifications { get; set; } = true;
    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;
    public DateTime? NextEligibilityReviewAt { get; set; }
    public int EligibilityReviewNumber { get; set; } = 1;
    public DateTime? TermsAcceptedAt { get; set; }
    public DateTime? AnonymizedAt { get; set; }
    public string? AnonymizationReason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Bank? Bank { get; set; }
}
