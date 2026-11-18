namespace Metro.Domain.Entities.Identity;

public class Operator
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Timezone { get; set; } = "Asia/Ho_Chi_Minh";
    public string Status { get; set; } = "ACTIVE"; // ACTIVE, INACTIVE
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<OperatorAccount> Accounts { get; set; } = new List<OperatorAccount>();
}
