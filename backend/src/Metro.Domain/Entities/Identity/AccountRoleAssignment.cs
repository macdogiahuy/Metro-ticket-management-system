using Metro.Domain.Common;

namespace Metro.Domain.Entities.Identity;

public class AccountRoleAssignment : IOperatorScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OperatorId { get; set; }
    public Guid AccountId { get; set; }
    public string RoleCode { get; set; } = string.Empty;
    public DateTime ValidFrom { get; set; } = DateTime.UtcNow;
    public DateTime? ValidTo { get; set; }
    public string? GrantedByUserId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime? RevokedAt { get; set; }
    public string? RevokedByUserId { get; set; }

    public Operator Operator { get; set; } = null!;
    public OperatorAccount Account { get; set; } = null!;
    public BusinessRole BusinessRole { get; set; } = null!;

    public TimeWindow GetValidityWindow() => new(ValidFrom, ValidTo);

    public bool IsActiveAt(DateTime timestampUtc) =>
        RevokedAt == null && GetValidityWindow().Contains(timestampUtc);
}
