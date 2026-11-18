namespace Metro.Domain.Entities.Identity;

public class RolePermission
{
    public string RoleCode { get; set; } = string.Empty;
    public string PermissionCode { get; set; } = string.Empty;
    public int Version { get; set; } = 1;

    public BusinessRole Role { get; set; } = null!;
    public Permission Permission { get; set; } = null!;
}
