using Metro.Domain.Common;

namespace Metro.Domain.Entities.Network;

public class LineStation : IOperatorScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OperatorId { get; set; }
    public Guid LineId { get; set; }
    public Guid StationId { get; set; }
    public short Direction { get; set; } // 0 or 1
    public short SequenceNo { get; set; }
    public int DistanceFromOriginM { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Line Line { get; set; } = null!;
    public Station Station { get; set; } = null!;
}
