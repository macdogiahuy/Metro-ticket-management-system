namespace Metro.Domain.Entities.Identity;

public class Bank
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsDemo { get; set; } = true;

    public ICollection<UserProfile> UserProfiles { get; set; } = new List<UserProfile>();
}
