using Metro.Application.Abstractions.Catalog;
using Metro.Application.Abstractions.MultiTenancy;
using Metro.Application.Catalog;
using Metro.Infrastructure.Persistence.Context;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Metro.Infrastructure.Catalog;

public sealed class CatalogService(
    ApplicationDbContext db,
    IOperatorContext operatorContext,
    IRouteFinder routeFinder,
    ICatalogCache cache,
    IOptions<CatalogCacheOptions> cacheOptions) : ICatalogService
{
    private const int MaxCacheableSearchLength = 64;

    private TimeSpan LinesAndStationsTtl => TimeSpan.FromSeconds(Math.Max(0, cacheOptions.Value.LinesAndStationsTtlSeconds));
    private TimeSpan JourneysTtl => TimeSpan.FromSeconds(Math.Max(0, cacheOptions.Value.JourneysTtlSeconds));
    private TimeSpan ServiceStatusTtl => TimeSpan.FromSeconds(Math.Max(0, cacheOptions.Value.ServiceStatusTtlSeconds));

    public async Task<LineCatalogResponse> GetLinesAsync(string? status, string? operatorCode, CancellationToken cancellationToken)
    {
        var normalizedStatus = ValidateStatusFilter(status);
        var op = await ResolveActiveOperatorAsync(operatorCode, cancellationToken);
        if (op is null) return new LineCatalogResponse(string.Empty, DateTime.UtcNow, []);

        return await cache.GetOrCreateAsync($"lines:{op.Id:N}:status={normalizedStatus ?? "all"}", LinesAndStationsTtl, async ct =>
        {
            var linesQuery = db.Lines.AsNoTracking();
            if (normalizedStatus is not null) linesQuery = linesQuery.Where(l => l.Status == normalizedStatus);
            var lines = await linesQuery.OrderBy(l => l.Code).ThenBy(l => l.Name).ToListAsync(ct);

            var allLineStations = await db.LineStations.AsNoTracking().Include(ls => ls.Station).ToListAsync(ct);
            var interchanges = ComputeInterchangeStations(allLineStations);
            var stopsByLine = allLineStations.GroupBy(ls => ls.LineId)
                .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Direction).ThenBy(x => x.SequenceNo).ToList());

            var items = lines.Select(line =>
            {
                var lineStops = stopsByLine.GetValueOrDefault(line.Id) ?? [];
                var stops = lineStops.Select(ls => new LineStopItem(
                    ls.StationId, ls.Station.Code, ls.Station.Name, ls.Station.StationType, ls.Station.Status,
                    ls.Direction, ls.SequenceNo, ls.TravelTimeSeconds, ls.DistanceFromOriginM, interchanges.Contains(ls.StationId))).ToArray();
                return new LineCatalogItem(line.Id, line.Code, line.Name, line.Status, line.OpenedOn,
                    lineStops.Select(s => s.StationId).Distinct().Count(), line.UpdatedAt, stops);
            }).ToArray();

            return new LineCatalogResponse(op.Code, DateTime.UtcNow, items);
        }, cancellationToken);
    }

    public async Task<StationCatalogResponse> GetStationsAsync(
        Guid? lineId, string? status, string? search, string? operatorCode, CancellationToken cancellationToken)
    {
        var normalizedStatus = ValidateStatusFilter(status);
        var normalizedSearch = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        var op = await ResolveActiveOperatorAsync(operatorCode, cancellationToken);
        if (op is null)
            return lineId.HasValue
                ? throw new CatalogException("LINE_NOT_FOUND", StatusCodes.Status404NotFound, "Line not found.")
                : new StationCatalogResponse(string.Empty, DateTime.UtcNow, []);

        async Task<StationCatalogResponse> LoadAsync(CancellationToken ct)
        {
            if (lineId.HasValue && !await db.Lines.AsNoTracking().AnyAsync(l => l.Id == lineId.Value, ct))
                throw new CatalogException("LINE_NOT_FOUND", StatusCodes.Status404NotFound, "Line not found.");

            var allLineStations = await db.LineStations.AsNoTracking().Include(ls => ls.Line).Include(ls => ls.Station).ToListAsync(ct);
            var interchanges = ComputeInterchangeStations(allLineStations);
            var memberships = allLineStations.GroupBy(ls => ls.StationId).ToDictionary(g => g.Key, g => g
                .OrderBy(x => x.Line.Code).ThenBy(x => x.Direction).ThenBy(x => x.SequenceNo)
                .Select(x => new StationLineMembership(x.LineId, x.Line.Code, x.Line.Name, x.Line.Status, x.Direction, x.SequenceNo, x.TravelTimeSeconds, x.DistanceFromOriginM))
                .ToArray());

            var query = db.Stations.AsNoTracking();
            if (normalizedStatus is not null) query = query.Where(s => s.Status == normalizedStatus);
            var stations = await query.ToListAsync(ct);

            if (lineId.HasValue)
            {
                var orderLookup = allLineStations.Where(ls => ls.LineId == lineId.Value).GroupBy(ls => ls.StationId)
                    .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Direction).ThenBy(x => x.SequenceNo).Select(x => (x.Direction, x.SequenceNo)).First());
                stations = stations.Where(s => orderLookup.ContainsKey(s.Id))
                    .OrderBy(s => orderLookup[s.Id].Direction).ThenBy(s => orderLookup[s.Id].SequenceNo).ToList();
            }
            else
            {
                stations = stations.OrderBy(s => s.Code, StringComparer.Ordinal).ThenBy(s => s.Name, StringComparer.Ordinal).ToList();
            }

            if (normalizedSearch is not null)
                stations = stations.Where(s => s.Name.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase)
                                            || s.Code.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase)).ToList();

            var items = stations.Select(s => new StationCatalogItem(
                s.Id, s.Code, s.Name, s.StationType, s.Status, s.Latitude, s.Longitude, s.MinTransferSeconds,
                interchanges.Contains(s.Id) || s.StationType.Equals("INTERCHANGE", StringComparison.OrdinalIgnoreCase),
                memberships.GetValueOrDefault(s.Id) ?? [])).ToArray();
            return new StationCatalogResponse(op.Code, DateTime.UtcNow, items);
        }

        if (normalizedSearch is { Length: > MaxCacheableSearchLength }) return await LoadAsync(cancellationToken);
        var searchKey = normalizedSearch is null ? "none" : Uri.EscapeDataString(normalizedSearch.ToLowerInvariant());
        var key = $"stations:{op.Id:N}:line={lineId?.ToString("N") ?? "all"}:status={normalizedStatus ?? "all"}:q={searchKey}";
        return await cache.GetOrCreateAsync(key, LinesAndStationsTtl, LoadAsync, cancellationToken);
    }

    public async Task<JourneySearchResponse> SearchJourneysAsync(
        Guid originStationId, Guid destinationStationId, string? operatorCode, CancellationToken cancellationToken)
    {
        if (originStationId == Guid.Empty || destinationStationId == Guid.Empty)
            throw new CatalogException("INVALID_JOURNEY_QUERY", StatusCodes.Status400BadRequest, "Both originStationId and destinationStationId are required.");
        if (originStationId == destinationStationId)
            throw new CatalogException("SAME_STATION", StatusCodes.Status400BadRequest, "Origin and destination stations must be different.");

        var op = await ResolveActiveOperatorAsync(operatorCode, cancellationToken)
            ?? throw new CatalogException("STATION_NOT_FOUND", StatusCodes.Status404NotFound, "Origin or destination station was not found.");

        return await cache.GetOrCreateAsync($"journeys:{op.Id:N}:{originStationId:N}:{destinationStationId:N}", JourneysTtl, async ct =>
        {
            var stations = await db.Stations.AsNoTracking().Select(s => new GraphStation(s.Id, s.Code, s.Name, s.Status, s.MinTransferSeconds)).ToListAsync(ct);
            var (origin, dest) = (stations.FirstOrDefault(s => s.Id == originStationId), stations.FirstOrDefault(s => s.Id == destinationStationId));
            if (origin is null || dest is null)
                throw new CatalogException("STATION_NOT_FOUND", StatusCodes.Status404NotFound, "Origin or destination station was not found.");

            var lines = await db.Lines.AsNoTracking().Select(l => new GraphLine(l.Id, l.Code, l.Name, l.Status)).ToListAsync(ct);
            var stops = await db.LineStations.AsNoTracking().Select(ls => new GraphLineStop(ls.LineId, ls.StationId, ls.Direction, ls.SequenceNo, ls.TravelTimeSeconds, ls.DistanceFromOriginM)).ToListAsync(ct);

            var options = routeFinder.FindOptimalRoutes(new NetworkGraphData(lines, stations, stops), originStationId, destinationStationId);
            if (options.Count == 0)
                throw new CatalogException("PATH_UNAVAILABLE", StatusCodes.Status422UnprocessableEntity,
                    "No serving metro route is currently available between the selected stations.");

            return new JourneySearchResponse(op.Code,
                new StationSummary(origin.Id, origin.Code, origin.Name, origin.Status),
                new StationSummary(dest.Id, dest.Code, dest.Name, dest.Status), DateTime.UtcNow, options);
        }, cancellationToken);
    }

    public async Task<ServiceStatusResponse> GetServiceStatusAsync(
        Guid? lineId, Guid? stationId, string? operatorCode, CancellationToken cancellationToken)
    {
        var op = await ResolveActiveOperatorAsync(operatorCode, cancellationToken);
        if (op is null)
        {
            if (lineId.HasValue) throw new CatalogException("LINE_NOT_FOUND", StatusCodes.Status404NotFound, "Line not found.");
            if (stationId.HasValue) throw new CatalogException("STATION_NOT_FOUND", StatusCodes.Status404NotFound, "Station not found.");
            return new ServiceStatusResponse(string.Empty, "NORMAL", DateTime.UtcNow, false, [], [], [], [], []);
        }

        var key = $"service-status:{op.Id:N}:line={lineId?.ToString("N") ?? "all"}:station={stationId?.ToString("N") ?? "all"}";
        return await cache.GetOrCreateAsync(key, ServiceStatusTtl, async ct =>
        {
            var now = DateTime.UtcNow;
            var allLines = await db.Lines.AsNoTracking().OrderBy(l => l.Code).ToListAsync(ct);
            var allStations = await db.Stations.AsNoTracking().OrderBy(s => s.Code).ToListAsync(ct);
            if (lineId.HasValue && !allLines.Any(l => l.Id == lineId.Value))
                throw new CatalogException("LINE_NOT_FOUND", StatusCodes.Status404NotFound, "Line not found.");
            if (stationId.HasValue && !allStations.Any(s => s.Id == stationId.Value))
                throw new CatalogException("STATION_NOT_FOUND", StatusCodes.Status404NotFound, "Station not found.");

            var links = await db.LineStations.AsNoTracking().Select(ls => new { ls.LineId, ls.StationId }).ToListAsync(ct);
            var lineIds = lineId.HasValue ? new HashSet<Guid> { lineId.Value }
                : stationId.HasValue ? links.Where(x => x.StationId == stationId.Value).Select(x => x.LineId).ToHashSet()
                : allLines.Select(l => l.Id).ToHashSet();
            var stationIds = stationId.HasValue ? new HashSet<Guid> { stationId.Value }
                : lineId.HasValue ? links.Where(x => x.LineId == lineId.Value).Select(x => x.StationId).ToHashSet()
                : allStations.Select(s => s.Id).ToHashSet();

            var incidents = (await LoadActiveIncidentsAsync(op.Id, ct)).Where(i =>
                (!lineId.HasValue || i.LineId == lineId.Value || (i.StationId.HasValue && stationIds.Contains(i.StationId.Value))) &&
                (!stationId.HasValue || i.StationId == stationId.Value || (i.StationId is null && i.LineId.HasValue && lineIds.Contains(i.LineId.Value)))).ToArray();
            var incidentIds = incidents.Select(i => i.Id).ToHashSet();

            var notices = (await LoadPublishedNoticesAsync(op.Id, now, ct))
                .Where(n => n.IncidentId is null || (!lineId.HasValue && !stationId.HasValue) || incidentIds.Contains(n.IncidentId.Value)).ToArray();

            var etaUpdates = (await LoadLatestManualEtasAsync(op.Id, ct))
                .Where(e => (!lineId.HasValue || e.LineId == lineId.Value) && (!stationId.HasValue || e.StationId == stationId.Value))
                .Select(e =>
                {
                    var (etaStatus, arrival, departure) = CatalogStatusRules.EvaluateManualEta(e.ExpectedArrivalAt, e.ExpectedDepartureAt, now);
                    return new TripEtaItem(e.PlannedTripId, e.TripCode, e.LineId, e.LineCode, e.Direction,
                        e.StationId, e.StationCode, e.StationName, etaStatus, arrival, departure, e.RecordedAt, e.Source, e.Note);
                }).ToArray();

            var lineItems = allLines.Where(l => lineIds.Contains(l.Id)).Select(l =>
            {
                var c = incidents.Count(i => i.LineId == l.Id);
                return new LineStatusItem(l.Id, l.Code, l.Name, l.Status, CatalogStatusRules.ComputeEntityOperationalState(l.Status, c), c, l.UpdatedAt);
            }).ToArray();
            var stationItems = allStations.Where(s => stationIds.Contains(s.Id)).Select(s =>
            {
                var c = incidents.Count(i => i.StationId == s.Id);
                return new StationStatusItem(s.Id, s.Code, s.Name, s.StationType, s.Status, CatalogStatusRules.ComputeEntityOperationalState(s.Status, c), c, s.UpdatedAt);
            }).ToArray();

            var overall = CatalogStatusRules.ComputeOverallStatus(lineItems.Select(l => l.OperationalState), stationItems.Select(s => s.OperationalState), incidents.Length);
            return new ServiceStatusResponse(op.Code, overall, now, etaUpdates.Any(e => e.EtaStatus == "UPDATED"), lineItems, stationItems, incidents, notices, etaUpdates);
        }, cancellationToken);
    }

    private async Task<ActiveOperator?> ResolveActiveOperatorAsync(string? operatorCode, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(operatorCode))
        {
            var code = operatorCode.Trim();
            var matched = await db.Operators.AsNoTracking().Where(o => o.Code == code && o.Status == "ACTIVE")
                .Select(o => new ActiveOperator(o.Id, o.Code)).SingleOrDefaultAsync(ct)
                ?? throw new CatalogException("OPERATOR_NOT_FOUND", StatusCodes.Status404NotFound, "Operator not found.");
            operatorContext.CurrentOperatorId = matched.Id;
            return matched;
        }
        if (operatorContext.CurrentOperatorId is { } id && id != Guid.Empty)
        {
            var current = await db.Operators.AsNoTracking().Where(o => o.Id == id && o.Status == "ACTIVE")
                .Select(o => new ActiveOperator(o.Id, o.Code)).SingleOrDefaultAsync(ct);
            if (current is not null) return current;
        }
        var defaultOp = await db.Operators.AsNoTracking().Where(o => o.Status == "ACTIVE")
            .OrderBy(o => o.CreatedAt).ThenBy(o => o.Id).Select(o => new ActiveOperator(o.Id, o.Code)).FirstOrDefaultAsync(ct);
        if (defaultOp is not null) operatorContext.CurrentOperatorId = defaultOp.Id;
        return defaultOp;
    }

    private Task<List<ServiceIncidentItem>> LoadActiveIncidentsAsync(Guid operatorId, CancellationToken ct) =>
        db.Database.SqlQuery<ServiceIncidentItem>($"""
            SELECT id AS "Id", code AS "Code", incident_type AS "IncidentType", severity AS "Severity", status AS "Status",
                   line_id AS "LineId", station_id AS "StationId", occurred_at AS "OccurredAt", summary AS "Summary",
                   impact_summary AS "ImpactSummary", position_text AS "PositionText", updated_at AS "UpdatedAt"
            FROM metro.incidents
            WHERE operator_id = {operatorId} AND status IN ('REPORTED', 'ASSESSED', 'IN_RESPONSE')
            ORDER BY occurred_at DESC
            """).ToListAsync(ct);

    private Task<List<ServiceNoticeItem>> LoadPublishedNoticesAsync(Guid operatorId, DateTime now, CancellationToken ct) =>
        db.Database.SqlQuery<ServiceNoticeItem>($"""
            SELECT id AS "Id", code AS "Code", incident_id AS "IncidentId", audience AS "Audience", title AS "Title",
                   content AS "Content", emergency_template AS "EmergencyTemplate", valid_from AS "ValidFrom",
                   valid_to AS "ValidTo", published_at AS "PublishedAt", updated_at AS "UpdatedAt"
            FROM metro.service_notices
            WHERE operator_id = {operatorId} AND status = 'PUBLISHED' AND valid_from <= {now}
              AND (valid_to IS NULL OR valid_to > {now}) AND UPPER(audience) NOT IN ('INTERNAL', 'ADMIN', 'STAFF_ONLY')
            ORDER BY emergency_template DESC, COALESCE(published_at, valid_from) DESC
            """).ToListAsync(ct);

    private Task<List<TripEtaRow>> LoadLatestManualEtasAsync(Guid operatorId, CancellationToken ct) =>
        db.Database.SqlQuery<TripEtaRow>($"""
            SELECT * FROM (
                SELECT DISTINCT ON (e.planned_trip_id, e.station_id)
                       e.planned_trip_id AS "PlannedTripId", pt.trip_code AS "TripCode", pt.line_id AS "LineId", l.code AS "LineCode",
                       pt.direction AS "Direction", e.station_id AS "StationId", s.code AS "StationCode", s.name AS "StationName",
                       e.expected_arrival_at AS "ExpectedArrivalAt", e.expected_departure_at AS "ExpectedDepartureAt",
                       e.recorded_at AS "RecordedAt", e.source AS "Source", e.note AS "Note"
                FROM metro.trip_eta_updates e
                JOIN metro.planned_trips pt ON pt.operator_id = e.operator_id AND pt.id = e.planned_trip_id
                JOIN metro.lines l ON l.operator_id = pt.operator_id AND l.id = pt.line_id
                JOIN metro.stations s ON s.operator_id = e.operator_id AND s.id = e.station_id
                WHERE e.operator_id = {operatorId} AND e.source = 'MANUAL' AND pt.status NOT IN ('COMPLETED', 'CANCELLED')
                ORDER BY e.planned_trip_id, e.station_id, e.recorded_at DESC
            ) latest ORDER BY COALESCE("ExpectedArrivalAt", "ExpectedDepartureAt") ASC NULLS LAST, "RecordedAt" DESC
            """).ToListAsync(ct);

    private static HashSet<Guid> ComputeInterchangeStations(IEnumerable<Domain.Entities.Network.LineStation> lineStations) =>
        lineStations.GroupBy(ls => ls.StationId)
            .Where(g => g.Select(x => x.LineId).Distinct().Count() > 1 || g.Any(x => x.Station.StationType.Equals("INTERCHANGE", StringComparison.OrdinalIgnoreCase)))
            .Select(g => g.Key).ToHashSet();

    private static string? ValidateStatusFilter(string? status) =>
        CatalogStatusRules.NormalizeAndValidateStatus(status) is var normalized && normalized != string.Empty
            ? normalized
            : throw new CatalogException("INVALID_STATUS", StatusCodes.Status400BadRequest, "Status must be one of: PLANNED, ACTIVE, SUSPENDED, CLOSED.");

    private sealed record ActiveOperator(Guid Id, string Code);
    private sealed record TripEtaRow(Guid PlannedTripId, string TripCode, Guid LineId, string LineCode, short Direction, Guid StationId, string StationCode, string StationName, DateTime? ExpectedArrivalAt, DateTime? ExpectedDepartureAt, DateTime RecordedAt, string Source, string? Note);
}
