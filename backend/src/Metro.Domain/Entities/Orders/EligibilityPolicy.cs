using Metro.Domain.Common;
using Metro.Domain.Entities.Identity;

namespace Metro.Domain.Entities.Orders;

public class EligibilityPolicy : IOperatorScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OperatorId { get; set; }
    public string Code { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
    public string BeneficiaryGroup { get; set; } = string.Empty; // STUDENT, ELDERLY, CHILD_UNDER_6
    public string DiscountType { get; set; } = "PERCENT"; // FREE, PERCENT, FIXED_AMOUNT
    public decimal DiscountValue { get; set; }
    public string[] EligibleProductCodes { get; set; } = [];
    public string[] RequiredEvidenceTypes { get; set; } = [];
    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public string Status { get; set; } = "DRAFT";
    public string CreatedByUserId { get; set; } = string.Empty;
    public string? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Operator Operator { get; set; } = null!;
    public ICollection<EligibilityApplication> Applications { get; set; } = new List<EligibilityApplication>();

    public TimeWindow GetEffectiveWindow() => new(EffectiveFrom, EffectiveTo);
    public bool IsEffectiveAt(DateTime timestampUtc) => Status == "PUBLISHED" && GetEffectiveWindow().Contains(timestampUtc);
}
