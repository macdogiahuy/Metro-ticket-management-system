using Metro.Domain.Common;

namespace Metro.Domain.Entities.Tickets;

public class TicketCredential : IOperatorScoped
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OperatorId { get; set; }
    public Guid TicketId { get; set; }
    public string CredentialType { get; set; } = "QR"; // QR, CITIZEN_ID_MOCK, NFC_MOCK
    public string TokenHash { get; set; } = string.Empty;
    public string? TokenLast4 { get; set; }
    public string Status { get; set; } = "ACTIVE"; // ACTIVE, SUSPENDED, REVOKED, REPLACED, EXPIRED
    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RevokedAt { get; set; }
    public Guid? ReplacedById { get; set; }

    public Ticket Ticket { get; set; } = null!;
    public TicketCredential? ReplacedBy { get; set; }
}
