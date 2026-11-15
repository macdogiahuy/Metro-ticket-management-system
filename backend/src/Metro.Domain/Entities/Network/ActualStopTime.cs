using Metro.Domain.Common;

namespace Metro.Domain.Entities.Network;

public class ActualStopTime : IOperatorScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OperatorId { get; set; }
    public Guid ActualTripId { get; set; }
    public Guid StationId { get; set; }
    public short SequenceNo { get; set; }
    public DateTime? ActualArrivalAt { get; set; }
    public DateTime? ActualDepartureAt { get; set; }
    public int? DwellSeconds { get; set; }
    public string? Irregularity { get; set; }

    public ActualTrip ActualTrip { get; set; } = null!;
    public Station Station { get; set; } = null!;
}
