using Metro.Domain.Common;

namespace Metro.Domain.Entities.Network;

public class PlannedStopTime : IOperatorScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OperatorId { get; set; }
    public Guid PlannedTripId { get; set; }
    public Guid StationId { get; set; }
    public short SequenceNo { get; set; }
    public DateTime PlannedArrivalAt { get; set; }
    public DateTime PlannedDepartureAt { get; set; }
    public int DwellSeconds { get; set; }

    public PlannedTrip PlannedTrip { get; set; } = null!;
    public Station Station { get; set; } = null!;
}
