namespace Metro.Application.Abstractions.Catalog;

/// <summary>BE-05 public catalog contracts: /api/v1/lines, /stations, /journeys, /service-status.</summary>
public sealed record LineCatalogResponse(string OperatorCode, DateTime DataAsOf, IReadOnlyList<LineCatalogItem> Lines);
public sealed record LineCatalogItem(Guid Id, string Code, string Name, string Status, DateOnly? OpenedOn, int StationCount, DateTime UpdatedAt, IReadOnlyList<LineStopItem> Stops);
public sealed record LineStopItem(Guid StationId, string StationCode, string StationName, string StationType, string StationStatus, short Direction, short SequenceNo, int TravelTimeSeconds, int DistanceFromOriginM, bool IsInterchange);

public sealed record StationCatalogResponse(string OperatorCode, DateTime DataAsOf, IReadOnlyList<StationCatalogItem> Stations);
public sealed record StationCatalogItem(Guid Id, string Code, string Name, string StationType, string Status, decimal? Latitude, decimal? Longitude, int MinTransferSeconds, bool IsInterchange, IReadOnlyList<StationLineMembership> Lines);
public sealed record StationLineMembership(Guid LineId, string LineCode, string LineName, string LineStatus, short Direction, short SequenceNo, int TravelTimeSeconds, int DistanceFromOriginM);

public sealed record JourneySearchResponse(string OperatorCode, StationSummary Origin, StationSummary Destination, DateTime DataAsOf, IReadOnlyList<JourneyOption> Options);
public sealed record StationSummary(Guid Id, string Code, string Name, string Status);
public sealed record JourneyOption(int OptionIndex, int SegmentCount, int TransferCount, int EstimatedDurationSeconds, int EstimatedDurationMinutes, int TotalDistanceMeters, IReadOnlyList<JourneyLeg> Legs, IReadOnlyList<JourneyTransfer> Transfers);
public sealed record JourneyLeg(Guid LineId, string LineCode, string LineName, short Direction, Guid FromStationId, string FromStationCode, string FromStationName, Guid ToStationId, string ToStationCode, string ToStationName, int SegmentCount, int TravelTimeSeconds, int DistanceMeters, IReadOnlyList<JourneyStop> Stations);
public sealed record JourneyStop(Guid StationId, string Code, string Name);
public sealed record JourneyTransfer(Guid StationId, string StationCode, string StationName, Guid FromLineId, string FromLineCode, Guid ToLineId, string ToLineCode, int MinTransferSeconds);

public sealed record ServiceStatusResponse(string OperatorCode, string OverallStatus, DateTime DataAsOf, bool EtaAvailable, IReadOnlyList<LineStatusItem> Lines, IReadOnlyList<StationStatusItem> Stations, IReadOnlyList<ServiceIncidentItem> ActiveIncidents, IReadOnlyList<ServiceNoticeItem> Notices, IReadOnlyList<TripEtaItem> EtaUpdates);
public sealed record LineStatusItem(Guid Id, string Code, string Name, string Status, string OperationalState, int ActiveIncidentCount, DateTime UpdatedAt);
public sealed record StationStatusItem(Guid Id, string Code, string Name, string StationType, string Status, string OperationalState, int ActiveIncidentCount, DateTime UpdatedAt);
public sealed record ServiceIncidentItem(Guid Id, string Code, string IncidentType, string Severity, string Status, Guid? LineId, Guid? StationId, DateTime OccurredAt, string Summary, string? ImpactSummary, string? PositionText, DateTime UpdatedAt);
public sealed record ServiceNoticeItem(Guid Id, string Code, Guid? IncidentId, string Audience, string Title, string Content, bool EmergencyTemplate, DateTime ValidFrom, DateTime? ValidTo, DateTime? PublishedAt, DateTime UpdatedAt);
public sealed record TripEtaItem(Guid PlannedTripId, string TripCode, Guid LineId, string LineCode, short Direction, Guid StationId, string StationCode, string StationName, string EtaStatus, DateTime? ExpectedArrivalAt, DateTime? ExpectedDepartureAt, DateTime RecordedAt, string Source, string? Note);

public sealed record GraphLine(Guid Id, string Code, string Name, string Status);
public sealed record GraphStation(Guid Id, string Code, string Name, string Status, int MinTransferSeconds);
public sealed record GraphLineStop(Guid LineId, Guid StationId, short Direction, short SequenceNo, int TravelTimeSeconds, int DistanceFromOriginM);
public sealed record NetworkGraphData(IReadOnlyList<GraphLine> Lines, IReadOnlyList<GraphStation> Stations, IReadOnlyList<GraphLineStop> LineStops);

public interface ICatalogService
{
    Task<LineCatalogResponse> GetLinesAsync(string? status, string? operatorCode, CancellationToken cancellationToken);
    Task<StationCatalogResponse> GetStationsAsync(Guid? lineId, string? status, string? search, string? operatorCode, CancellationToken cancellationToken);
    Task<JourneySearchResponse> SearchJourneysAsync(Guid originStationId, Guid destinationStationId, string? operatorCode, CancellationToken cancellationToken);
    Task<ServiceStatusResponse> GetServiceStatusAsync(Guid? lineId, Guid? stationId, string? operatorCode, CancellationToken cancellationToken);
}

public interface IRouteFinder
{
    IReadOnlyList<JourneyOption> FindOptimalRoutes(NetworkGraphData graph, Guid originStationId, Guid destinationStationId);
}

public interface ICatalogCache
{
    Task<T> GetOrCreateAsync<T>(string key, TimeSpan ttl, Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken);
    Task RemoveAsync(string key, CancellationToken cancellationToken);
    Task InvalidateOperatorAsync(Guid operatorId, CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>Stable machine-readable error for ProblemDetails extension "code".</summary>
public sealed class CatalogException(string code, int status, string message) : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
}

public sealed class CatalogCacheOptions
{
    public bool Enabled { get; set; } = true;
    public string KeyPrefix { get; set; } = "catalog:v1:";
    public int LinesAndStationsTtlSeconds { get; set; } = 60;
    public int JourneysTtlSeconds { get; set; } = 60;
    public int ServiceStatusTtlSeconds { get; set; } = 20;
}
