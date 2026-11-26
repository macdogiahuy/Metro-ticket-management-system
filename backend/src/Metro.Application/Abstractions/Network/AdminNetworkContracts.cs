namespace Metro.Application.Abstractions.Network;

/// <summary>BE-13 UC24 admin network contracts: /api/v1/admin/lines and /api/v1/admin/stations.</summary>
public sealed record CreateLineRequest(string Code, string Name, string? Status, DateOnly? OpenedOn, string? Reason);
public sealed record UpdateLineRequest(string Name, string Status, DateOnly? OpenedOn, string? Reason);
public sealed record LineStopItemRequest(Guid StationId, short SequenceNo, int DistanceFromOriginM, int TravelTimeSeconds);
public sealed record SetLineStationsRequest(short Direction, IReadOnlyList<LineStopItemRequest> Stops, string? Reason);

public sealed record AdminLineStopResponse(
    Guid Id, Guid StationId, string StationCode, string StationName, string StationStatus,
    short Direction, short SequenceNo, int DistanceFromOriginM, int TravelTimeSeconds);

public sealed record AdminLineResponse(
    Guid Id, string Code, string Name, string Status, DateOnly? OpenedOn,
    DateTime CreatedAt, DateTime UpdatedAt, IReadOnlyList<AdminLineStopResponse> Stops);

public sealed record CreateStationRequest(
    string Code, string Name, string? StationType, decimal? Latitude, decimal? Longitude,
    int? MinTransferSeconds, string? Status, string? Reason);

public sealed record UpdateStationRequest(
    string Name, string StationType, decimal? Latitude, decimal? Longitude,
    int MinTransferSeconds, string Status, string? Reason);

public sealed record AdminStationResponse(
    Guid Id, string Code, string Name, string StationType, decimal? Latitude, decimal? Longitude,
    int MinTransferSeconds, string Status, DateTime CreatedAt, DateTime UpdatedAt);

public interface IAdminNetworkService
{
    Task<IReadOnlyList<AdminLineResponse>> ListLinesAsync(string? status, string adminUserId, CancellationToken ct);
    Task<AdminLineResponse> GetLineAsync(Guid lineId, string adminUserId, CancellationToken ct);
    Task<AdminLineResponse> CreateLineAsync(CreateLineRequest request, string adminUserId, CancellationToken ct);
    Task<AdminLineResponse> UpdateLineAsync(Guid lineId, UpdateLineRequest request, string adminUserId, CancellationToken ct);
    Task DeleteLineAsync(Guid lineId, string? reason, string adminUserId, CancellationToken ct);
    Task<AdminLineResponse> SetLineStationsAsync(Guid lineId, SetLineStationsRequest request, string adminUserId, CancellationToken ct);
    Task<IReadOnlyList<AdminStationResponse>> ListStationsAsync(string? status, string? search, string adminUserId, CancellationToken ct);
    Task<AdminStationResponse> GetStationAsync(Guid stationId, string adminUserId, CancellationToken ct);
    Task<AdminStationResponse> CreateStationAsync(CreateStationRequest request, string adminUserId, CancellationToken ct);
    Task<AdminStationResponse> UpdateStationAsync(Guid stationId, UpdateStationRequest request, string adminUserId, CancellationToken ct);
    Task DeleteStationAsync(Guid stationId, string? reason, string adminUserId, CancellationToken ct);
}
