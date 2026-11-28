namespace Metro.Application.Abstractions.Network;

/// <summary>BE-13 UC29/UC37/UC38 contracts under /api/v1/admin/trip-load (max_load_kg, manual load kg, manual ETA).</summary>
public sealed record TrainsetCarRequest(string Code, short SequenceNo, string? CarType, decimal MaxLoadKg);
public sealed record CreateTrainsetRequest(string Code, string? DepotCode, string? Status, IReadOnlyList<TrainsetCarRequest> Cars, string? Reason);
public sealed record UpdateTrainsetRequest(string? DepotCode, string Status, IReadOnlyList<TrainsetCarRequest>? Cars, string? Reason);

public sealed record AdminCarResponse(Guid Id, string Code, short SequenceNo, string? CarType, decimal? MaxLoadKg);
public sealed record AdminTrainsetResponse(Guid Id, string Code, string? DepotCode, string Status, decimal TotalCarMaxLoadKg, DateTime UpdatedAt, IReadOnlyList<AdminCarResponse> Cars);

public sealed record CreateLoadProfileRequest(string Code, Guid? LineId, Guid? TrainsetId, decimal MaxLoadKg, DateTime EffectiveFrom, DateTime? EffectiveTo, string? Reason);
public sealed record LoadProfileDecisionRequest(string? Reason);

public sealed record AdminLoadProfileResponse(
    Guid Id, string Code, int Version, Guid? LineId, Guid? TrainsetId, decimal? MaxLoadKg,
    string Status, DateTime EffectiveFrom, DateTime? EffectiveTo, string CreatedByUserId,
    string? ApprovedByUserId, DateTime? ApprovedAt, DateTime CreatedAt, DateTime UpdatedAt);

public sealed record RecordTripLoadObservationRequest(Guid PlannedTripId, decimal ObservedLoadKg, DateTime? ObservedAt, string? Note);
public sealed record AdminTripLoadObservationResponse(
    Guid Id, Guid PlannedTripId, Guid? CapacityProfileId, decimal ObservedLoadKg, decimal? MaxLoadKg,
    string LoadStatus, string Source, string RecordedByUserId, DateTime ObservedAt, DateTime RecordedAt, string? Note);

public sealed record AdminTripLoadStatusResponse(
    Guid PlannedTripId, string TripCode, DateOnly ServiceDate, string BoardingStatus,
    decimal? ObservedLoadKg, DateTime? ObservedAt, decimal? MaxLoadKg, string LoadStatus);

public sealed record RecordTripEtaUpdateRequest(Guid PlannedTripId, Guid StationId, DateTime? ExpectedArrivalAt, DateTime? ExpectedDepartureAt, string? Note);
public sealed record AdminTripEtaUpdateResponse(
    Guid Id, Guid PlannedTripId, Guid StationId, DateTime? ExpectedArrivalAt, DateTime? ExpectedDepartureAt,
    string Source, string RecordedByUserId, DateTime RecordedAt, string? Note);

public interface IAdminTripLoadService
{
    Task<IReadOnlyList<AdminTrainsetResponse>> ListTrainsetsAsync(string adminUserId, CancellationToken ct);
    Task<AdminTrainsetResponse> CreateTrainsetAsync(CreateTrainsetRequest request, string adminUserId, CancellationToken ct);
    Task<AdminTrainsetResponse> UpdateTrainsetAsync(Guid trainsetId, UpdateTrainsetRequest request, string adminUserId, CancellationToken ct);
    Task<IReadOnlyList<AdminLoadProfileResponse>> ListLoadProfilesAsync(Guid? trainsetId, string? status, string adminUserId, CancellationToken ct);
    Task<AdminLoadProfileResponse> CreateLoadProfileAsync(CreateLoadProfileRequest request, string adminUserId, CancellationToken ct);
    Task<AdminLoadProfileResponse> ApproveLoadProfileAsync(Guid profileId, LoadProfileDecisionRequest request, string adminUserId, CancellationToken ct);
    Task<AdminLoadProfileResponse> PublishLoadProfileAsync(Guid profileId, LoadProfileDecisionRequest request, string adminUserId, CancellationToken ct);
    Task<IReadOnlyList<AdminTripLoadStatusResponse>> GetTripLoadStatusesAsync(Guid? tripId, Guid? lineId, DateOnly? serviceDate, string adminUserId, CancellationToken ct);
    Task<AdminTripLoadObservationResponse> RecordLoadObservationAsync(RecordTripLoadObservationRequest request, string adminUserId, CancellationToken ct);
    Task<AdminTripEtaUpdateResponse> RecordEtaUpdateAsync(RecordTripEtaUpdateRequest request, string adminUserId, CancellationToken ct);
}
