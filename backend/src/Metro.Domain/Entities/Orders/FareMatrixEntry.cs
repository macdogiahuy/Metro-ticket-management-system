using Metro.Domain.Common;
using Metro.Domain.Entities.Network;

namespace Metro.Domain.Entities.Orders;

public class FareMatrixEntry : IOperatorScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OperatorId { get; set; }
    public Guid FarePolicyVersionId { get; set; }
    public Guid TicketProductId { get; set; }
    public Guid? LineId { get; set; }
    public Guid? OriginStationId { get; set; }
    public Guid? DestinationStationId { get; set; }
    public string PaymentChannel { get; set; } = "ANY"; // CASH, NON_CASH, ANY
    public string EligibilityGroup { get; set; } = "ORDINARY";
    public long AmountMinor { get; set; }
    public bool Sellable { get; set; } = true;

    public FarePolicyVersion FarePolicyVersion { get; set; } = null!;
    public TicketProduct TicketProduct { get; set; } = null!;
    public Line? Line { get; set; }
    public Station? OriginStation { get; set; }
    public Station? DestinationStation { get; set; }
}
