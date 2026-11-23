using Metro.Domain.Common;
using Metro.Domain.Entities.Network;
using Metro.Domain.Entities.Orders;

namespace Metro.Domain.Entities.Tickets;

public class Ticket : IOperatorScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OperatorId { get; set; }
    public string TicketCode { get; set; } = string.Empty;
    public Guid OrderItemId { get; set; }
    public Guid TicketProductId { get; set; }
    public string? HolderUserId { get; set; }
    public int SequenceNo { get; set; }
    public string Status { get; set; } = "ISSUED"; // ISSUED, ACTIVE, IN_USE, USED, EXPIRED, REFUND_PENDING, REFUNDED, REVOKED
    public DateTime? EffectiveFrom { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? LatestActivationAt { get; set; }
    public DateTime? FirstUsedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? RevocationReason { get; set; }
    public Guid FarePolicyVersionId { get; set; }
    public Guid CorrelationId { get; set; } = Guid.NewGuid();
    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Added in SRS v1.2 (migration 006)
    public Guid? OrderId { get; set; }
    public int? OrderVersion { get; set; }
    public DateOnly? ServiceDate { get; set; }
    public Guid? OriginStationId { get; set; }
    public Guid? DestinationStationId { get; set; }
    public Guid? RelatedTicketId { get; set; }
    public string? RelationType { get; set; } // CHILD_OF_GUARDIAN, GUARDIAN_OF_CHILD
    public Guid? GuardianLinkId { get; set; }
    public string? GuardianUserId { get; set; }
    public short ChangeCount { get; set; } = 0;

    public OrderItem OrderItem { get; set; } = null!;
    public TicketProduct TicketProduct { get; set; } = null!;
    public FarePolicyVersion FarePolicyVersion { get; set; } = null!;
    public Order? Order { get; set; }
    public Station? OriginStation { get; set; }
    public Station? DestinationStation { get; set; }
    public Ticket? RelatedTicket { get; set; }
    public ICollection<TicketCredential> Credentials { get; set; } = new List<TicketCredential>();

    /// <summary>
    /// Returns [EffectiveFrom, ExpiresAt); each NULL bound is unbounded.
    /// EndExclusive semantics: lower bound is inclusive, upper bound is exclusive.
    /// </summary>
    public TimeWindow GetValidityWindow() => new(EffectiveFrom, ExpiresAt);

    /// <summary>
    /// Checks validity at the specified UTC instant.
    /// Returns true iff the ticket status is usable and timestamp is within [EffectiveFrom, ExpiresAt).
    /// </summary>
    public bool IsValidAt(DateTime timestampUtc)
    {
        // ISSUED is the SQL-created record awaiting QR readiness in BE-09.
        if (Status != "ACTIVE" && Status != "IN_USE")
        {
            return false;
        }

        return GetValidityWindow().Contains(timestampUtc);
    }

    /// <summary>
    /// Evaluates if this ticket overlaps in validity with another ticket for the same passenger.
    /// Used by the application layer to enforce "chặn trùng hiệu lực" (BR-007 / ex_tickets_no_overlapping_validity).
    /// </summary>
    public bool OverlapsValidity(Ticket other)
    {
        var thisWindow = GetValidityWindow();
        var otherWindow = other.GetValidityWindow();

        return thisWindow.Overlaps(otherWindow);
    }
}
