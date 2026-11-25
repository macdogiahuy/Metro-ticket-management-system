namespace Metro.Application.Abstractions.Gates;

public sealed record GateQrActor(string UserId, Guid SessionId);
public sealed record GateQrChallengeRequest(Guid RequestId, Guid OperationId, string Action,
    string FullName, DateOnly BirthDate, string Reason);
public sealed record GateQrAccessRequest(Guid RequestId, Guid OperationId, string Action,
    string FullName, DateOnly BirthDate, string Reason, string VerificationMethod,
    Guid? ChallengeId = null, string? EmailCode = null, bool NoEmailAccess = false,
    bool DocumentMatchesProfile = false, string? EvidenceReference = null);
public sealed record GateQrReceipt(Guid RequestId, Guid OperationId, Guid TicketId,
    string Status, string Code, Guid? ChallengeId = null, DateTimeOffset? ExpiresAt = null,
    Guid? CredentialId = null, int? Version = null, string? DeliveryStatus = null);
public interface IGateQrSupportService
{
    Task<GateQrReceipt> ChallengeAsync(GateQrActor actor, string? operatorCode, Guid ticketId, GateQrChallengeRequest request, string key, CancellationToken ct);
    Task<GateQrReceipt> AccessAsync(GateQrActor actor, string? operatorCode, Guid ticketId, GateQrAccessRequest request, string key, CancellationToken ct);
    Task<GateQrReceipt> GetAsync(GateQrActor actor, string? operatorCode, Guid requestId, CancellationToken ct);
    Task<byte[]> ImageAsync(GateQrActor actor, string? operatorCode, Guid requestId, CancellationToken ct);
}

// Transport receives secrets only in memory, after decrypting the private outbox.
public sealed record GateEmailMessage(Guid DeliveryId, string Recipient, string Subject, string Body);
public interface IGateEmailSender
{
    Task SendAsync(GateEmailMessage message, CancellationToken ct);
}
