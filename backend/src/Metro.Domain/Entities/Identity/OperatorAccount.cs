using Metro.Domain.Common;

namespace Metro.Domain.Entities.Identity;

public class OperatorAccount : IOperatorScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OperatorId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string AccountType { get; set; } = "PASSENGER"; // PASSENGER, STAFF, ADMIN
    public string Status { get; set; } = "ACTIVE"; // ACTIVE, LOCKED, DISABLED, CLOSED
    public bool MfaRequired { get; set; }

    public Operator Operator { get; set; } = null!;
    public ICollection<AccountRoleAssignment> RoleAssignments { get; set; } = new List<AccountRoleAssignment>();
}
