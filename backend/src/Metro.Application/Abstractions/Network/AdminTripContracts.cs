namespace Metro.Application.Abstractions.Network;

/// <summary>BE-13 UC29 admin planned trip, stop-time, actual-stop, and trainset allocation contracts.</summary>
public sealed record TripStopTimeRequest(
    Guid StationId, short SequenceNo, DateTime PlannedArrivalAt, DateTime PlannedDepartureAt, int DwellSeconds);

public sealed record CreateTripRequest(
    Guid ScheduleVersionId, Guid LineId, string TripCode, DateOnly ServiceDate, short Direction,
    DateTime PlannedStartAt, DateTime PlannedEndAt, Guid? LoadProfileId, Guid? TrainsetId,
    IReadOnlyList<TripStopTimeRequest>? StopTimes, string? Reason);

public sealed record UpdateTripRequest(
    string Status, Guid? BoardingStationId, string? BoardingStatus, string? CancellationReason,
    Guid? LoadProfileId, DateTime? ActualArrivalAt, DateTime? ActualDepartureAt, string? Reason);

public sealed record AllocateTripTrainsetRequest(
    Guid TrainsetId, DateTime? AllocatedFrom, DateTime? AllocatedTo, Guid? LoadProfileId, string? Reason);

public sealed record AdminTripStopResponse(
    Guid Id, Guid StationId, string StationCode, string StationName, short SequenceNo,
    DateTime PlannedArrivalAt, DateTime PlannedDepartureAt, int DwellSeconds);

public sealed record AdminTripAllocationResponse(
    Guid Id, Guid TrainsetId, string TrainsetCode, DateTime AllocatedFrom, DateTime AllocatedTo, string Status, DateTime CreatedAt);

public sealed record AdminTripResponse(
    Guid Id, Guid ScheduleVersionId, Guid LineId, string TripCode, DateOnly ServiceDate, short Direction,
    DateTime PlannedStartAt, DateTime PlannedEndAt, string Status, Guid? LoadProfileId, Guid? BoardingStationId,
    string BoardingStatus, string? CancellationReason, DateTime CreatedAt, DateTime UpdatedAt,
    IReadOnlyList<AdminTripStopResponse> StopTimes, AdminTripAllocationResponse? ActiveAllocation);

public interface IAdminTripService
{
    Task<IReadOnlyList<AdminTripResponse>> ListTripsAsync(Guid? lineId, DateOnly? serviceDate, string? status, Guid? scheduleVersionId, string adminUserId, CancellationToken ct);
    Task<AdminTripResponse> GetTripAsync(Guid tripId, string adminUserId, CancellationToken ct);
    Task<AdminTripResponse> CreateTripAsync(CreateTripRequest request, string adminUserId, CancellationToken ct);
    Task<AdminTripResponse> UpdateTripAsync(Guid tripId, UpdateTripRequest request, string adminUserId, CancellationToken ct);
    Task<AdminTripAllocationResponse> AllocateTrainsetAsync(Guid tripId, AllocateTripTrainsetRequest request, string adminUserId, CancellationToken ct);
}
