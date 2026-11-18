namespace Metro.Domain.Entities.Identity;

public class BusinessRole
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ActorCode { get; set; }
    public bool IsPrivileged { get; set; }

    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
    public ICollection<AccountRoleAssignment> AccountRoleAssignments { get; set; } = new List<AccountRoleAssignment>();
}
