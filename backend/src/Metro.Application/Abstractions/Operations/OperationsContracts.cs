namespace Metro.Application.Abstractions.Operations;

public sealed record UpdateEtaRequest(Guid StationId, DateTimeOffset? ExpectedArrivalAt,
    DateTimeOffset? ExpectedDepartureAt, string Reason);
public sealed record RecordLoadRequest(decimal ObservedLoadKg, DateTimeOffset ObservedAt, string Reason);
public sealed record UpdateTripStateRequest(string Status, string Reason, string? BoardingStatus = null);
public sealed record CreateDisruptionRequest(string Title, string Description, string Effect,
    DateTimeOffset OccurredAt, Guid? LineId = null, Guid? StationId = null, Guid? PlannedTripId = null);
public sealed record DisruptionDecisionRequest(bool Confirmed, string Reason);
public sealed record OperationReceipt(Guid Id, string Status, DateTimeOffset RecordedAt,
    bool SuggestionsRefreshPending = true);
public sealed record TripLoadResponse(Guid PlannedTripId, string TripCode, DateOnly ServiceDate,
    string BoardingStatus, decimal? ObservedLoadKg, DateTimeOffset? ObservedAt, decimal? MaxLoadKg,
    string LoadStatus, DateTimeOffset? RecordedAt, string Source = "MANUAL")
{
    public bool EntryLoadAllowed => LoadStatus == "AVAILABLE" && BoardingStatus == "OPEN";
    public bool HoldsCapacity => false;
}
public sealed record EtaHistoryItem(Guid Id, Guid StationId, DateTimeOffset? ExpectedArrivalAt,
    DateTimeOffset? ExpectedDepartureAt, DateTimeOffset RecordedAt, string RecordedByUserId);
public sealed record LoadHistoryItem(Guid Id, decimal ObservedLoadKg, DateTimeOffset ObservedAt,
    DateTimeOffset RecordedAt, string RecordedByUserId);
public sealed record TripOperationsHistory(IReadOnlyList<EtaHistoryItem> EtaUpdates,
    IReadOnlyList<LoadHistoryItem> LoadObservations);
public sealed record DisruptionResponse(Guid Id, string Title, string Description, string Effect, string Status,
    Guid? LineId, Guid? StationId, Guid? PlannedTripId, DateTimeOffset OccurredAt,
    DateTimeOffset? PublishedAt, DateTimeOffset? ResolvedAt);

public interface IOperationsService
{
    Task<OperationReceipt> UpdateEtaAsync(string actor, string operatorCode, Guid tripId, UpdateEtaRequest request, string key, CancellationToken ct);
    Task<OperationReceipt> RecordLoadAsync(string actor, string operatorCode, Guid tripId, RecordLoadRequest request, string key, CancellationToken ct);
    Task<OperationReceipt> UpdateTripStateAsync(string actor, string operatorCode, Guid tripId, UpdateTripStateRequest request, string key, CancellationToken ct);
    Task<OperationReceipt> CreateDisruptionAsync(string actor, string operatorCode, CreateDisruptionRequest request, string key, CancellationToken ct);
    Task<OperationReceipt> DecideDisruptionAsync(string actor, string operatorCode, Guid id, DisruptionDecisionRequest request, bool resolve, string key, CancellationToken ct);
    Task<TripLoadResponse> GetLoadAsync(string operatorCode, Guid tripId, CancellationToken ct);
    Task<TripOperationsHistory> GetHistoryAsync(string actor, string operatorCode, Guid tripId, int page, CancellationToken ct);
    Task<IReadOnlyList<DisruptionResponse>> ListDisruptionsAsync(string actor, string operatorCode, int page, CancellationToken ct);
}

public sealed class OperationsException(string code, int status) : Exception(code)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
}
