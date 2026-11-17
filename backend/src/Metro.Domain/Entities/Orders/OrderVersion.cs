using Metro.Domain.Common;
using Metro.Domain.Entities.Network;

namespace Metro.Domain.Entities.Orders;

public class OrderVersion : IOperatorScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OperatorId { get; set; }
    public Guid OrderId { get; set; }
    public int Version { get; set; } = 1;
    public string Status { get; set; } = "DRAFT"; // DRAFT, CONFIRMED, SUPERSEDED, CANCELLED
    public DateOnly? ServiceDate { get; set; }
    public Guid TicketProductId { get; set; }
    public Guid? OriginStationId { get; set; }
    public Guid? DestinationStationId { get; set; }
    public string? BeneficiaryUserId { get; set; }
    public string? EligibilityGroup { get; set; }
    public DateOnly? AgeReferenceDate { get; set; }
    public long SubtotalMinor { get; set; }
    public long DiscountMinor { get; set; }
    public long TotalMinor { get; set; }
    public Guid FarePolicyVersionId { get; set; }
    public string FareSnapshot { get; set; } = "{}";
    public string? ChangeReason { get; set; }
    public string? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ConfirmedAt { get; set; }

    public Order Order { get; set; } = null!;
    public TicketProduct TicketProduct { get; set; } = null!;
    public Station? OriginStation { get; set; }
    public Station? DestinationStation { get; set; }
    public FarePolicyVersion FarePolicyVersion { get; set; } = null!;
}
