namespace Metro.Application.Abstractions.Gates;

public sealed record GateScanRequest(Guid RequestId, string Action, Guid StationId,
    Guid? PlannedTripId, short? Direction, string[] QrTokens, string DeviceCode,
    bool PairPresentConfirmed = false);

public sealed record GateResult(Guid RequestId, string Decision, string ReasonCode,
    DateTimeOffset? ProcessedAt, IReadOnlyList<GatePassage> Passages);

public sealed record GatePassage(Guid TicketId, Guid? JourneyId, Guid EventId);

// Evidence is an internal reference/description, never an uploaded document or QR token.
public sealed record GateResolutionRequest(Guid RequestId, Guid SourceRequestId, string Action,
    Guid StationId, string Reason, string EvidenceReference, bool IdentityVerified,
    bool PairPresentConfirmed = false, bool PassengerHasNotTravelled = false,
    DateTimeOffset? ProvenEnteredAt = null, Guid? EntryStationId = null,
    Guid? EntryPlannedTripId = null, short? EntryDirection = null);

public sealed record GateLookupRequest(string TicketCode, string Reason, string? Phone = null);
public sealed record GateTicketLookup(Guid TicketId, string TicketCode, string Status,
    string ProductType, DateTimeOffset? EffectiveFrom, DateTimeOffset? ExpiresAt,
    Guid? OriginStationId, Guid? DestinationStationId, Guid? OpenJourneyId,
    Guid? PairedJourneyId, string? PassengerName);
public sealed record GateEscalation(Guid RequestId, Guid SourceRequestId, string ActorUserId, DateTimeOffset CreatedAt,
    string Reason, string EvidenceReference, string Status);
public sealed record GateEscalationPage(IReadOnlyList<GateEscalation> Items,int Page,int PageSize,long Total);

public interface IGateService
{
    Task<GateResult> ScanAsync(string actor, string? operatorCode, GateScanRequest request, string key, CancellationToken ct);
    Task<GateResult> ResultAsync(string actor, string? operatorCode, Guid requestId, CancellationToken ct);
    Task<GateResult> ResolveAsync(string actor, string? operatorCode, GateResolutionRequest request, string key, CancellationToken ct);
    Task<GateTicketLookup> LookupAsync(string actor, string? operatorCode, GateLookupRequest request, CancellationToken ct);
    Task<GateEscalationPage> EscalationsAsync(string actor,string? operatorCode,int page,string status,CancellationToken ct);
}

public sealed class GateException(string code, int status) : Exception(code)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
}
