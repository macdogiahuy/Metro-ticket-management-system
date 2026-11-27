using Metro.Application.Abstractions.Catalog;
using Metro.Application.Abstractions.MultiTenancy;
using Metro.Application.Abstractions.Network;
using Metro.Application.Network;
using Metro.Domain.Entities.Network;
using Metro.Infrastructure.Persistence.Context;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Metro.Infrastructure.Network;

public sealed class AdminNetworkService(
    ApplicationDbContext db,
    IOperatorContext operatorContext,
    ICatalogCache catalogCache) : IAdminNetworkService
{
    public async Task<IReadOnlyList<AdminLineResponse>> ListLinesAsync(string? status, string adminUserId, CancellationToken ct)
    {
        await NetworkAdminHelper.ResolveAdminOperatorAsync(db, operatorContext, adminUserId, ct);
        var query = db.Lines.AsNoTracking().Include(l => l.LineStations).ThenInclude(ls => ls.Station).AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalized = NetworkAdminRules.NormalizeMasterStatus(status);
            query = query.Where(l => l.Status == normalized);
        }
        return (await query.OrderBy(l => l.Code).ThenBy(l => l.Name).ToListAsync(ct)).Select(ToLineResponse).ToArray();
    }

    public async Task<AdminLineResponse> GetLineAsync(Guid lineId, string adminUserId, CancellationToken ct)
    {
        await NetworkAdminHelper.ResolveAdminOperatorAsync(db, operatorContext, adminUserId, ct);
        var line = await db.Lines.AsNoTracking().Include(l => l.LineStations).ThenInclude(ls => ls.Station)
            .SingleOrDefaultAsync(l => l.Id == lineId, ct)
            ?? throw new NetworkAdminException("LINE_NOT_FOUND", StatusCodes.Status404NotFound, "Line was not found.");
        return ToLineResponse(line);
    }

    public async Task<AdminLineResponse> CreateLineAsync(CreateLineRequest request, string adminUserId, CancellationToken ct)
    {
        var opId = await NetworkAdminHelper.ResolveAdminOperatorAsync(db, operatorContext, adminUserId, ct);
        var code = NetworkAdminRules.RequireText(request.Code, 40, "LINE_CODE_REQUIRED", "Line code is required (max 40 chars).");
        var name = NetworkAdminRules.RequireText(request.Name, 200, "LINE_NAME_REQUIRED", "Line name is required (max 200 chars).");
        var status = string.IsNullOrWhiteSpace(request.Status) ? "ACTIVE" : NetworkAdminRules.NormalizeMasterStatus(request.Status);
        var now = DateTime.UtcNow;
        var entity = new Line
        {
            OperatorId = opId, Code = code, Name = name, Status = status, OpenedOn = request.OpenedOn,
            CreatedByUserId = adminUserId, UpdatedByUserId = adminUserId, CreatedAt = now, UpdatedAt = now,
        };
        db.Lines.Add(entity);
        NetworkAdminHelper.AddAuditLog(db, opId, adminUserId, "CREATE_LINE", "LINE", entity.Id, ["code", "name", "status", "opened_on"], null, LineSnapshot(entity), request.Reason);
        await SaveMappedAsync("LINE_CODE_DUPLICATE", $"Line code '{code}' already exists.", ct);
        await catalogCache.InvalidateOperatorAsync(opId, ct);
        return ToLineResponse(entity);
    }

    public async Task<AdminLineResponse> UpdateLineAsync(Guid lineId, UpdateLineRequest request, string adminUserId, CancellationToken ct)
    {
        var opId = await NetworkAdminHelper.ResolveAdminOperatorAsync(db, operatorContext, adminUserId, ct);
        var entity = await db.Lines.Include(l => l.LineStations).ThenInclude(ls => ls.Station).SingleOrDefaultAsync(l => l.Id == lineId, ct)
            ?? throw new NetworkAdminException("LINE_NOT_FOUND", StatusCodes.Status404NotFound, "Line was not found.");
        var before = LineSnapshot(entity);
        entity.Name = NetworkAdminRules.RequireText(request.Name, 200, "LINE_NAME_REQUIRED", "Line name is required (max 200 chars).");
        entity.Status = NetworkAdminRules.NormalizeMasterStatus(request.Status);
        entity.OpenedOn = request.OpenedOn;
        entity.UpdatedByUserId = adminUserId;
        entity.UpdatedAt = DateTime.UtcNow;
        NetworkAdminHelper.AddAuditLog(db, opId, adminUserId, "UPDATE_LINE", "LINE", entity.Id, ["name", "status", "opened_on"], before, LineSnapshot(entity), request.Reason);
        await SaveMappedAsync("LINE_CONFLICT", "Line update conflicted.", ct);
        await catalogCache.InvalidateOperatorAsync(opId, ct);
        return ToLineResponse(entity);
    }

    public async Task DeleteLineAsync(Guid lineId, string? reason, string adminUserId, CancellationToken ct)
    {
        var opId = await NetworkAdminHelper.ResolveAdminOperatorAsync(db, operatorContext, adminUserId, ct);
        var entity = await db.Lines.SingleOrDefaultAsync(l => l.Id == lineId, ct)
            ?? throw new NetworkAdminException("LINE_NOT_FOUND", StatusCodes.Status404NotFound, "Line was not found.");
        if (entity.Status != "PLANNED" || await IsLineReferencedAsync(opId, lineId, ct))
            throw new NetworkAdminException("LINE_IN_USE", StatusCodes.Status409Conflict, "Referenced or active lines cannot be deleted (BR-018); set status to SUSPENDED or CLOSED instead.");
        var before = LineSnapshot(entity);
        db.Lines.Remove(entity);
        NetworkAdminHelper.AddAuditLog(db, opId, adminUserId, "DELETE_LINE", "LINE", entity.Id, ["deleted"], before, null, reason);
        await SaveMappedAsync("LINE_IN_USE", "Line is referenced and cannot be deleted.", ct);
        await catalogCache.InvalidateOperatorAsync(opId, ct);
    }

    public async Task<AdminLineResponse> SetLineStationsAsync(Guid lineId, SetLineStationsRequest request, string adminUserId, CancellationToken ct)
    {
        var opId = await NetworkAdminHelper.ResolveAdminOperatorAsync(db, operatorContext, adminUserId, ct);
        var line = await db.Lines.SingleOrDefaultAsync(l => l.Id == lineId, ct)
            ?? throw new NetworkAdminException("LINE_NOT_FOUND", StatusCodes.Status404NotFound, "Line was not found.");
        var direction = NetworkAdminRules.ValidateDirection(request.Direction);
        var inputs = (request.Stops ?? []).Select(s => new LineStopRuleInput(s.StationId, s.SequenceNo, s.DistanceFromOriginM, s.TravelTimeSeconds)).ToArray();
        NetworkAdminRules.ValidateLineStops(inputs);

        var stationIds = inputs.Select(s => s.StationId).Distinct().ToArray();
        if (await db.Stations.AsNoTracking().CountAsync(s => stationIds.Contains(s.Id), ct) != stationIds.Length)
            throw new NetworkAdminException("STATION_NOT_FOUND", StatusCodes.Status404NotFound, "One or more stations were not found in the current operator.");

        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var existing = await db.LineStations.Where(ls => ls.LineId == lineId && ls.Direction == direction).ToListAsync(ct);
            var before = existing.OrderBy(x => x.SequenceNo).Select(x => new { x.StationId, x.SequenceNo, x.DistanceFromOriginM, x.TravelTimeSeconds }).ToArray();
            if (existing.Count > 0)
            {
                db.LineStations.RemoveRange(existing);
                await db.SaveChangesAsync(ct);
            }
            var now = DateTime.UtcNow;
            db.LineStations.AddRange(inputs.OrderBy(x => x.SequenceNo).Select(s => new LineStation
            {
                OperatorId = opId, LineId = lineId, StationId = s.StationId, Direction = direction,
                SequenceNo = s.SequenceNo, DistanceFromOriginM = s.DistanceFromOriginM, TravelTimeSeconds = s.TravelTimeSeconds, CreatedAt = now,
            }));
            line.UpdatedByUserId = adminUserId;
            line.UpdatedAt = now;
            NetworkAdminHelper.AddAuditLog(db, opId, adminUserId, "SET_LINE_STATIONS", "LINE", lineId, ["line_stations", "travel_time_seconds", "distance_from_origin_m"], before, inputs, request.Reason);
            await SaveMappedAsync("LINE_STATION_CONFLICT", "Duplicate station or sequence on line direction.", ct);
            await tx.CommitAsync(ct);
        });
        await catalogCache.InvalidateOperatorAsync(opId, ct);
        return await GetLineAsync(lineId, adminUserId, ct);
    }

    public async Task<IReadOnlyList<AdminStationResponse>> ListStationsAsync(string? status, string? search, string adminUserId, CancellationToken ct)
    {
        await NetworkAdminHelper.ResolveAdminOperatorAsync(db, operatorContext, adminUserId, ct);
        var query = db.Stations.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalized = NetworkAdminRules.NormalizeMasterStatus(status);
            query = query.Where(s => s.Status == normalized);
        }
        var stations = await query.OrderBy(s => s.Code).ThenBy(s => s.Name).ToListAsync(ct);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var q = search.Trim();
            stations = stations.Where(s => s.Code.Contains(q, StringComparison.OrdinalIgnoreCase) || s.Name.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        return stations.Select(ToStationResponse).ToArray();
    }

    public async Task<AdminStationResponse> GetStationAsync(Guid stationId, string adminUserId, CancellationToken ct)
    {
        await NetworkAdminHelper.ResolveAdminOperatorAsync(db, operatorContext, adminUserId, ct);
        var station = await db.Stations.AsNoTracking().SingleOrDefaultAsync(s => s.Id == stationId, ct)
            ?? throw new NetworkAdminException("STATION_NOT_FOUND", StatusCodes.Status404NotFound, "Station was not found.");
        return ToStationResponse(station);
    }

    public async Task<AdminStationResponse> CreateStationAsync(CreateStationRequest request, string adminUserId, CancellationToken ct)
    {
        var opId = await NetworkAdminHelper.ResolveAdminOperatorAsync(db, operatorContext, adminUserId, ct);
        var code = NetworkAdminRules.RequireText(request.Code, 40, "STATION_CODE_REQUIRED", "Station code is required (max 40 chars).");
        var name = NetworkAdminRules.RequireText(request.Name, 200, "STATION_NAME_REQUIRED", "Station name is required (max 200 chars).");
        var transfer = request.MinTransferSeconds ?? 300;
        NetworkAdminRules.ValidateNonNegativeSeconds(transfer, "INVALID_MIN_TRANSFER_SECONDS", "minTransferSeconds");
        NetworkAdminRules.ValidateCoordinates(request.Latitude, request.Longitude);
        var now = DateTime.UtcNow;
        var entity = new Station
        {
            OperatorId = opId, Code = code, Name = name,
            StationType = string.IsNullOrWhiteSpace(request.StationType) ? "ELEVATED" : NetworkAdminRules.NormalizeStationType(request.StationType),
            Latitude = request.Latitude, Longitude = request.Longitude, MinTransferSeconds = transfer,
            Status = string.IsNullOrWhiteSpace(request.Status) ? "ACTIVE" : NetworkAdminRules.NormalizeMasterStatus(request.Status),
            CreatedByUserId = adminUserId, UpdatedByUserId = adminUserId, CreatedAt = now, UpdatedAt = now,
        };
        db.Stations.Add(entity);
        NetworkAdminHelper.AddAuditLog(db, opId, adminUserId, "CREATE_STATION", "STATION", entity.Id, ["code", "name", "station_type", "latitude", "longitude", "min_transfer_seconds", "status"], null, StationSnapshot(entity), request.Reason);
        await SaveMappedAsync("STATION_CODE_DUPLICATE", $"Station code '{code}' already exists.", ct);
        await catalogCache.InvalidateOperatorAsync(opId, ct);
        return ToStationResponse(entity);
    }

    public async Task<AdminStationResponse> UpdateStationAsync(Guid stationId, UpdateStationRequest request, string adminUserId, CancellationToken ct)
    {
        var opId = await NetworkAdminHelper.ResolveAdminOperatorAsync(db, operatorContext, adminUserId, ct);
        var entity = await db.Stations.SingleOrDefaultAsync(s => s.Id == stationId, ct)
            ?? throw new NetworkAdminException("STATION_NOT_FOUND", StatusCodes.Status404NotFound, "Station was not found.");
        NetworkAdminRules.ValidateNonNegativeSeconds(request.MinTransferSeconds, "INVALID_MIN_TRANSFER_SECONDS", "minTransferSeconds");
        NetworkAdminRules.ValidateCoordinates(request.Latitude, request.Longitude);
        var before = StationSnapshot(entity);
        entity.Name = NetworkAdminRules.RequireText(request.Name, 200, "STATION_NAME_REQUIRED", "Station name is required (max 200 chars).");
        entity.StationType = NetworkAdminRules.NormalizeStationType(request.StationType);
        entity.Latitude = request.Latitude;
        entity.Longitude = request.Longitude;
        entity.MinTransferSeconds = request.MinTransferSeconds;
        entity.Status = NetworkAdminRules.NormalizeMasterStatus(request.Status);
        entity.UpdatedByUserId = adminUserId;
        entity.UpdatedAt = DateTime.UtcNow;
        NetworkAdminHelper.AddAuditLog(db, opId, adminUserId, "UPDATE_STATION", "STATION", entity.Id, ["name", "station_type", "latitude", "longitude", "min_transfer_seconds", "status"], before, StationSnapshot(entity), request.Reason);
        await SaveMappedAsync("STATION_CONFLICT", "Station update conflicted.", ct);
        await catalogCache.InvalidateOperatorAsync(opId, ct);
        return ToStationResponse(entity);
    }

    public async Task DeleteStationAsync(Guid stationId, string? reason, string adminUserId, CancellationToken ct)
    {
        var opId = await NetworkAdminHelper.ResolveAdminOperatorAsync(db, operatorContext, adminUserId, ct);
        var entity = await db.Stations.SingleOrDefaultAsync(s => s.Id == stationId, ct)
            ?? throw new NetworkAdminException("STATION_NOT_FOUND", StatusCodes.Status404NotFound, "Station was not found.");
        if (entity.Status != "PLANNED" || await IsStationReferencedAsync(opId, stationId, ct))
            throw new NetworkAdminException("STATION_IN_USE", StatusCodes.Status409Conflict, "Referenced or active stations cannot be deleted (BR-018); set status to SUSPENDED or CLOSED instead.");
        var before = StationSnapshot(entity);
        db.Stations.Remove(entity);
        NetworkAdminHelper.AddAuditLog(db, opId, adminUserId, "DELETE_STATION", "STATION", entity.Id, ["deleted"], before, null, reason);
        await SaveMappedAsync("STATION_IN_USE", "Station is referenced and cannot be deleted.", ct);
        await catalogCache.InvalidateOperatorAsync(opId, ct);
    }

    private Task<bool> IsLineReferencedAsync(Guid opId, Guid lineId, CancellationToken ct) =>
        db.Database.SqlQuery<bool>($"""
            SELECT (EXISTS (SELECT 1 FROM metro.line_stations WHERE operator_id = {opId} AND line_id = {lineId})
                 OR EXISTS (SELECT 1 FROM metro.schedule_versions WHERE operator_id = {opId} AND line_id = {lineId})
                 OR EXISTS (SELECT 1 FROM metro.planned_trips WHERE operator_id = {opId} AND line_id = {lineId})
                 OR EXISTS (SELECT 1 FROM metro.fare_matrix_entries WHERE operator_id = {opId} AND line_id = {lineId})
                 OR EXISTS (SELECT 1 FROM metro.capacity_profiles WHERE operator_id = {opId} AND line_id = {lineId})
                 OR EXISTS (SELECT 1 FROM metro.incidents WHERE operator_id = {opId} AND line_id = {lineId})) AS "Value"
            """).SingleAsync(ct);

    private Task<bool> IsStationReferencedAsync(Guid opId, Guid stationId, CancellationToken ct) =>
        db.Database.SqlQuery<bool>($"""
            SELECT (EXISTS (SELECT 1 FROM metro.line_stations WHERE operator_id = {opId} AND station_id = {stationId})
                 OR EXISTS (SELECT 1 FROM metro.planned_stop_times WHERE operator_id = {opId} AND station_id = {stationId})
                 OR EXISTS (SELECT 1 FROM metro.fare_matrix_entries WHERE operator_id = {opId} AND (origin_station_id = {stationId} OR destination_station_id = {stationId}))
                 OR EXISTS (SELECT 1 FROM metro.order_items WHERE operator_id = {opId} AND (origin_station_id = {stationId} OR destination_station_id = {stationId}))
                 OR EXISTS (SELECT 1 FROM metro.tickets WHERE operator_id = {opId} AND (origin_station_id = {stationId} OR destination_station_id = {stationId}))
                 OR EXISTS (SELECT 1 FROM metro.journeys WHERE operator_id = {opId} AND (planned_origin_station_id = {stationId} OR planned_destination_station_id = {stationId} OR actual_entry_station_id = {stationId} OR actual_exit_station_id = {stationId}))
                 OR EXISTS (SELECT 1 FROM metro.incidents WHERE operator_id = {opId} AND station_id = {stationId})) AS "Value"
            """).SingleAsync(ct);

    private async Task SaveMappedAsync(string dupCode, string dupMsg, CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) { throw NetworkAdminHelper.MapPostgresException(ex, dupCode, dupMsg); }
    }

    private static object LineSnapshot(Line l) => new { l.Id, l.Code, l.Name, l.Status, l.OpenedOn };
    private static object StationSnapshot(Station s) => new { s.Id, s.Code, s.Name, s.StationType, s.Latitude, s.Longitude, s.MinTransferSeconds, s.Status };
    private static AdminLineResponse ToLineResponse(Line l) => new(
        l.Id, l.Code, l.Name, l.Status, l.OpenedOn, l.CreatedAt, l.UpdatedAt,
        l.LineStations.OrderBy(s => s.Direction).ThenBy(s => s.SequenceNo).Select(s => new AdminLineStopResponse(
            s.Id, s.StationId, s.Station?.Code ?? string.Empty, s.Station?.Name ?? string.Empty, s.Station?.Status ?? string.Empty,
            s.Direction, s.SequenceNo, s.DistanceFromOriginM, s.TravelTimeSeconds)).ToArray());
    private static AdminStationResponse ToStationResponse(Station s) => new(
        s.Id, s.Code, s.Name, s.StationType, s.Latitude, s.Longitude, s.MinTransferSeconds, s.Status, s.CreatedAt, s.UpdatedAt);
}
