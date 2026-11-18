using Metro.Domain.Common;
using Metro.Domain.Entities.Network;
using Metro.Domain.Entities.Tickets;

namespace Metro.Domain.Entities.Orders;

public class OrderItem : IOperatorScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OperatorId { get; set; }
    public Guid OrderId { get; set; }
    public Guid TicketProductId { get; set; }
    public Guid? PlannedTripId { get; set; }
    public Guid? OriginStationId { get; set; }
    public Guid? DestinationStationId { get; set; }
    public int PassengerCount { get; set; } = 1;
    public long UnitPriceMinor { get; set; }
    public long DiscountMinor { get; set; }
    public long LineTotalMinor { get; set; }
    public Guid? EligibilityApplicationId { get; set; }
    public string ProductSnapshot { get; set; } = "{}";
    public string FareSnapshot { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Order Order { get; set; } = null!;
    public TicketProduct TicketProduct { get; set; } = null!;
    public PlannedTrip? PlannedTrip { get; set; }
    public Station? OriginStation { get; set; }
    public Station? DestinationStation { get; set; }
    public ICollection<Ticket> Tickets { get; set; } = new List<Ticket>();
}
