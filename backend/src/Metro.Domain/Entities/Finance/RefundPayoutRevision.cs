using Metro.Domain.Common;

namespace Metro.Domain.Entities.Finance;

public sealed class RefundPayoutRevision : IOperatorScoped
{
    public Guid Id { get; set; }
    public Guid OperatorId { get; set; }
    public Guid RefundRequestId { get; set; }
    public int RevisionNo { get; set; }
    public string? BankCode { get; set; }
    public byte[]? AccountCiphertext { get; set; }
    public string? AccountLast4 { get; set; }
    public string? HolderName { get; set; }
    public string ChangedByUserId { get; set; } = null!;
    public string ChangeReason { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }

    public RefundRequest RefundRequest { get; set; } = null!;
}
