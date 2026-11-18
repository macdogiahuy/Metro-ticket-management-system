using Metro.Domain.Common;
using Metro.Domain.Entities.Identity;

namespace Metro.Domain.Entities.Orders;

public class TicketProduct : IOperatorScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OperatorId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ProductType { get; set; } = "SINGLE_TRIP"; // SINGLE_TRIP, DAY, WEEK, MONTH, MULTI_MONTH
    public int? DurationValue { get; set; }
    public string? DurationUnit { get; set; } // DAY, MONTH
    public string ExpiryRule { get; set; } = "SINGLE_JOURNEY"; // SINGLE_JOURNEY, NEXT_LOCAL_MIDNIGHT, ELAPSED_DURATION
    public TimeSpan? LatestActivationInterval { get; set; }
    public bool RequiresNamedHolder { get; set; }
    public bool CapacityControlled { get; set; }
    public string Status { get; set; } = "ACTIVE"; // DRAFT, ACTIVE, SUSPENDED, RETIRED
    public string? CreatedByUserId { get; set; }
    public string? UpdatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Operator Operator { get; set; } = null!;
    public ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
    public ICollection<OrderVersion> OrderVersions { get; set; } = new List<OrderVersion>();
}
