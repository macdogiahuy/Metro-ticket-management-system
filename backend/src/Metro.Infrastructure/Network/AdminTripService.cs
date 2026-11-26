using Metro.Application.Abstractions.Catalog;
using Metro.Application.Abstractions.MultiTenancy;
using Metro.Application.Abstractions.Network;
using Metro.Application.Network;
using Metro.Domain.Entities.Network;
using Metro.Infrastructure.Persistence.Context;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Metro.Infrastructure.Network;

public sealed class AdminTripService(
    ApplicationDbContext db,
    IOperatorContext operatorContext,
    ICatalogCache catalogCache) : IAdminTripService
{
    public async Task<IReadOnlyList<AdminTripResponse>> ListTripsAsync(
        Guid? lineId, DateOnly? serviceDate, string? status, Guid? scheduleVersionId, string adminUserId, CancellationToken ct)
    {
        var opId = await NetworkAdminHelper.ResolveAdminOperatorAsync(db, operatorContext, adminUserId, ct);
        var q = db.PlannedTrips.AsNoTracking().Include(t => t.StopTimes).ThenInclude(s => s.Station).AsQueryable();
        if (lineId.HasValue) q = q.Where(t => t.LineId == lineId.Value);
        if (serviceDate.HasValue) q = q.Where(t => t.ServiceDate == serviceDate.Value);
        if (scheduleVersionId.HasValue) q = q.Where(t => t.ScheduleVersionId == scheduleVersionId.Value);
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(t => t.Status == NetworkAdminRules.NormalizeTripStatus(status));
        var trips = await q.OrderBy(t => t.ServiceDate).ThenBy(t => t.PlannedStartAt).Take(200).ToListAsync(ct);
        var allocations = await LoadActiveAllocationsAsync(opId, trips.Select(t => t.Id).ToArray(), ct);
        return trips.Select(t => ToTripResponse(t, allocations.GetValueOrDefault(t.Id))).ToArray();
    }

    public async Task<AdminTripResponse> GetTripAsync(Guid tripId, string adminUserId, CancellationToken ct)
    {
        var opId = await NetworkAdminHelper.ResolveAdminOperatorAsync(db, operatorContext, adminUserId, ct);
        var trip = await db.PlannedTrips.AsNoTracking().Include(t => t.StopTimes).ThenInclude(s => s.Station)
            .SingleOrDefaultAsync(t => t.Id == tripId, ct)
            ?? throw new NetworkAdminException("TRIP_NOT_FOUND", StatusCodes.Status404NotFound, "Planned trip was not found.");
        var allocs = await LoadActiveAllocationsAsync(opId, [trip.Id], ct);
        return ToTripResponse(trip, allocs.GetValueOrDefault(trip.Id));
    }

    public async Task<AdminTripResponse> CreateTripAsync(CreateTripRequest req, string adminUserId, CancellationToken ct)
    {
        var opId = await NetworkAdminHelper.ResolveAdminOperatorAsync(db, operatorContext, adminUserId, ct);
        var code = NetworkAdminRules.RequireText(req.TripCode, 60, "TRIP_CODE_REQUIRED", "tripCode is required (max 60 chars).");
        var direction = NetworkAdminRules.ValidateDirection(req.Direction);
        var startUtc = NetworkAdminRules.ToUtc(req.PlannedStartAt);
        var endUtc = NetworkAdminRules.ToUtc(req.PlannedEndAt);
        if (endUtc <= startUtc)
            throw new NetworkAdminException("INVALID_TRIP_WINDOW", StatusCodes.Status400BadRequest, "plannedEndAt must be strictly after plannedStartAt.");

        var schedule = await db.ScheduleVersions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == req.ScheduleVersionId, ct)
            ?? throw new NetworkAdminException("SCHEDULE_NOT_FOUND", StatusCodes.Status404NotFound, "Schedule version was not found.");
        if (schedule.LineId != req.LineId)
            throw new NetworkAdminException("SCHEDULE_LINE_MISMATCH", StatusCodes.Status400BadRequest, "Schedule version does not belong to the requested line.");
        if (req.LoadProfileId.HasValue && !await LoadProfileExistsAsync(opId, req.LoadProfileId.Value, ct))
            throw new NetworkAdminException("LOAD_PROFILE_NOT_FOUND", StatusCodes.Status404NotFound, "Load profile was not found.");

        var stopInputs = req.StopTimes is { Count: > 0 }
            ? req.StopTimes.Select(s => new TripStopTimeRuleInput(s.StationId, s.SequenceNo, NetworkAdminRules.ToUtc(s.PlannedArrivalAt), NetworkAdminRules.ToUtc(s.PlannedDepartureAt), s.DwellSeconds)).ToArray()
            : await AutoBuildStopTimesAsync(req.LineId, direction, startUtc, endUtc, ct);
        NetworkAdminRules.ValidateTripStopTimes(startUtc, endUtc, stopInputs);
        var stopStationIds = stopInputs.Select(s => s.StationId).Distinct().ToArray();
        if (stopStationIds.Length > 0 && await db.Stations.AsNoTracking().CountAsync(s => stopStationIds.Contains(s.Id), ct) != stopStationIds.Length)
            throw new NetworkAdminException("STATION_NOT_FOUND", StatusCodes.Status404NotFound, "One or more stop stations were not found.");

        var now = DateTime.UtcNow;
        var trip = new PlannedTrip
        {
            OperatorId = opId, ScheduleVersionId = req.ScheduleVersionId, LineId = req.LineId, TripCode = code,
            ServiceDate = req.ServiceDate, Direction = direction, PlannedStartAt = startUtc, PlannedEndAt = endUtc,
            Status = "PLANNED", LoadProfileId = req.LoadProfileId, BoardingStationId = stopInputs.FirstOrDefault()?.StationId,
            BoardingStatus = "OPEN", CreatedAt = now, UpdatedAt = now,
        };

        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            db.PlannedTrips.Add(trip);
            db.PlannedStopTimes.AddRange(stopInputs.Select(s => new PlannedStopTime
            {
                OperatorId = opId, PlannedTripId = trip.Id, StationId = s.StationId, SequenceNo = s.SequenceNo,
                PlannedArrivalAt = s.PlannedArrivalAt, PlannedDepartureAt = s.PlannedDepartureAt, DwellSeconds = s.DwellSeconds,
            }));
            NetworkAdminHelper.AddAuditLog(db, opId, adminUserId, "CREATE_PLANNED_TRIP", "PLANNED_TRIP", trip.Id,
                ["trip_code", "service_date", "direction", "planned_start_at", "planned_end_at", "load_profile_id"], null, TripSnapshot(trip), req.Reason);
            await SaveMappedAsync("TRIP_CODE_DUPLICATE", $"Trip '{code}' already exists on {req.ServiceDate:yyyy-MM-dd}.", ct);
            if (req.TrainsetId.HasValue)
                await InsertAllocationSqlAsync(opId, trip.Id, req.TrainsetId.Value, startUtc, endUtc, adminUserId, ct);
            await tx.CommitAsync(ct);
        });

        await catalogCache.InvalidateOperatorAsync(opId, ct);
        return await GetTripAsync(trip.Id, adminUserId, ct);
    }

    public async Task<AdminTripResponse> UpdateTripAsync(Guid tripId, UpdateTripRequest req, string adminUserId, CancellationToken ct)
    {
        var opId = await NetworkAdminHelper.ResolveAdminOperatorAsync(db, operatorContext, adminUserId, ct);
        var trip = await db.PlannedTrips.Include(t => t.StopTimes).SingleOrDefaultAsync(t => t.Id == tripId, ct)
            ?? throw new NetworkAdminException("TRIP_NOT_FOUND", StatusCodes.Status404NotFound, "Planned trip was not found.");
        var nextStatus = NetworkAdminRules.NormalizeTripStatus(req.Status);
        NetworkAdminRules.ValidateTripStatusTransition(trip.Status, nextStatus, req.CancellationReason);

        var nextBoarding = string.IsNullOrWhiteSpace(req.BoardingStatus)
            ? (nextStatus is "CANCELLED" or "COMPLETED" or "DISRUPTED" ? "CLOSED" : trip.BoardingStatus)
            : NetworkAdminRules.NormalizeBoardingStatus(req.BoardingStatus);
        if (nextStatus is "CANCELLED" or "COMPLETED" or "DISRUPTED" && nextBoarding == "OPEN")
            throw new NetworkAdminException("INVALID_BOARDING_STATUS", StatusCodes.Status409Conflict, $"Cannot keep boarding OPEN when trip status is '{nextStatus}'.");

        if (req.LoadProfileId.HasValue && req.LoadProfileId != trip.LoadProfileId)
        {
            if (trip.Status == "IN_SERVICE" && trip.BoardingStatus == "OPEN" && nextBoarding == "OPEN")
                throw new NetworkAdminException("BOARDING_MUST_PAUSE_FOR_SWAP", StatusCodes.Status409Conflict, "Pause boarding before changing load profile on an in-service trip (BR-056).");
            if (!await LoadProfileExistsAsync(opId, req.LoadProfileId.Value, ct))
                throw new NetworkAdminException("LOAD_PROFILE_NOT_FOUND", StatusCodes.Status404NotFound, "Load profile was not found.");
        }
        if (req.BoardingStationId.HasValue)
        {
            if (trip.StopTimes.Count > 0 && !trip.StopTimes.Any(s => s.StationId == req.BoardingStationId.Value))
                throw new NetworkAdminException("STATION_NOT_ON_TRIP", StatusCodes.Status400BadRequest, "boardingStationId must belong to the trip's scheduled stops.");
            if (!await db.Stations.AsNoTracking().AnyAsync(s => s.Id == req.BoardingStationId.Value, ct))
                throw new NetworkAdminException("STATION_NOT_FOUND", StatusCodes.Status404NotFound, "Boarding station was not found.");
        }

        var before = TripSnapshot(trip);
        var now = DateTime.UtcNow;
        trip.Status = nextStatus;
        trip.BoardingStatus = nextBoarding;
        if (req.BoardingStationId.HasValue) trip.BoardingStationId = req.BoardingStationId;
        if (req.LoadProfileId.HasValue) trip.LoadProfileId = req.LoadProfileId;
        if (nextStatus == "CANCELLED") trip.CancellationReason = req.CancellationReason!.Trim();
        trip.UpdatedAt = now;

        if ((req.ActualArrivalAt.HasValue || req.ActualDepartureAt.HasValue) && trip.BoardingStationId.HasValue)
            await UpsertActualStopAsync(opId, trip, trip.BoardingStationId.Value, req.ActualArrivalAt, req.ActualDepartureAt, req.Reason, now, ct);

        NetworkAdminHelper.AddAuditLog(db, opId, adminUserId, "UPDATE_PLANNED_TRIP", "PLANNED_TRIP", trip.Id,
            ["status", "boarding_station_id", "boarding_status", "load_profile_id", "cancellation_reason"], before, TripSnapshot(trip), req.Reason);
        await SaveMappedAsync("TRIP_CONFLICT", "Trip update conflicted.", ct);
        await catalogCache.InvalidateOperatorAsync(opId, ct);
        return await GetTripAsync(trip.Id, adminUserId, ct);
    }

    public async Task<AdminTripAllocationResponse> AllocateTrainsetAsync(Guid tripId, AllocateTripTrainsetRequest req, string adminUserId, CancellationToken ct)
    {
        var opId = await NetworkAdminHelper.ResolveAdminOperatorAsync(db, operatorContext, adminUserId, ct);
        var trip = await db.PlannedTrips.SingleOrDefaultAsync(t => t.Id == tripId, ct)
            ?? throw new NetworkAdminException("TRIP_NOT_FOUND", StatusCodes.Status404NotFound, "Planned trip was not found.");
        if (trip.Status is "COMPLETED" or "CANCELLED")
            throw new NetworkAdminException("TRIP_ALREADY_ENDED", StatusCodes.Status409Conflict, "Cannot allocate a trainset to a completed or cancelled trip.");

        var fromUtc = req.AllocatedFrom.HasValue ? NetworkAdminRules.ToUtc(req.AllocatedFrom.Value) : trip.PlannedStartAt;
        var toUtc = req.AllocatedTo.HasValue ? NetworkAdminRules.ToUtc(req.AllocatedTo.Value) : trip.PlannedEndAt;
        if (toUtc <= fromUtc)
            throw new NetworkAdminException("INVALID_ALLOCATION_WINDOW", StatusCodes.Status400BadRequest, "allocatedTo must be strictly after allocatedFrom.");

        var trainset = await LoadTrainsetBriefAsync(opId, req.TrainsetId, ct)
            ?? throw new NetworkAdminException("TRAINSET_NOT_FOUND", StatusCodes.Status404NotFound, "Trainset was not found.");
        if (trainset.Status is "MAINTENANCE" or "OUT_OF_SERVICE")
            throw new NetworkAdminException("TRAINSET_UNAVAILABLE", StatusCodes.Status409Conflict, $"Trainset '{trainset.Code}' is '{trainset.Status}' and cannot be allocated (BR-053).");
        if (req.LoadProfileId.HasValue && !await LoadProfileExistsAsync(opId, req.LoadProfileId.Value, ct))
            throw new NetworkAdminException("LOAD_PROFILE_NOT_FOUND", StatusCodes.Status404NotFound, "Load profile was not found.");

        var allocationId = Guid.Empty;
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var activeCount = await db.Database.SqlQuery<int>($"""
                SELECT count(*)::int AS "Value" FROM metro.trip_allocations
                WHERE operator_id = {opId} AND planned_trip_id = {tripId} AND status = 'ALLOCATED'
                """).SingleAsync(ct);
            if (activeCount > 0 && trip.Status == "IN_SERVICE" && trip.BoardingStatus == "OPEN")
                throw new NetworkAdminException("BOARDING_MUST_PAUSE_FOR_SWAP", StatusCodes.Status409Conflict, "Pause boarding before replacing the trainset on an in-service trip (BR-056).");
            if (activeCount > 0)
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE metro.trip_allocations SET status = 'RELEASED'
                    WHERE operator_id = {opId} AND planned_trip_id = {tripId} AND status = 'ALLOCATED'
                    """, ct);
            }
            allocationId = await InsertAllocationSqlAsync(opId, tripId, req.TrainsetId, fromUtc, toUtc, adminUserId, ct);
            if (req.LoadProfileId.HasValue) { trip.LoadProfileId = req.LoadProfileId.Value; trip.UpdatedAt = DateTime.UtcNow; }
            NetworkAdminHelper.AddAuditLog(db, opId, adminUserId, "ALLOCATE_TRIP_TRAINSET", "PLANNED_TRIP", tripId,
                ["trainset_id", "allocated_from", "allocated_to", "load_profile_id"], null, new { allocationId, req.TrainsetId, fromUtc, toUtc, req.LoadProfileId }, req.Reason);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });

        await catalogCache.InvalidateOperatorAsync(opId, ct);
        return new AdminTripAllocationResponse(allocationId, trainset.Id, trainset.Code, fromUtc, toUtc, "ALLOCATED", DateTime.UtcNow);
    }

    private async Task<TripStopTimeRuleInput[]> AutoBuildStopTimesAsync(Guid lineId, short direction, DateTime startUtc, DateTime endUtc, CancellationToken ct)
    {
        var stops = await db.LineStations.AsNoTracking().Where(ls => ls.LineId == lineId && ls.Direction == direction).OrderBy(ls => ls.SequenceNo).ToListAsync(ct);
        if (stops.Count == 0 && direction == 1)
            stops = await db.LineStations.AsNoTracking().Where(ls => ls.LineId == lineId && ls.Direction == 0).OrderByDescending(ls => ls.SequenceNo).ToListAsync(ct);
        if (stops.Count == 0) return [];
        var result = new List<TripStopTimeRuleInput>(stops.Count);
        var cursor = startUtc;
        for (short i = 0; i < stops.Count; i++)
        {
            if (i > 0) cursor = cursor.AddSeconds(Math.Max(0, stops[i].TravelTimeSeconds));
            if (cursor > endUtc) cursor = endUtc;
            result.Add(new TripStopTimeRuleInput(stops[i].StationId, (short)(i + 1), cursor, cursor, 0));
        }
        return result.ToArray();
    }

    private async Task UpsertActualStopAsync(Guid opId, PlannedTrip trip, Guid stationId, DateTime? arr, DateTime? dep, string? reason, DateTime now, CancellationToken ct)
    {
        var actual = await db.ActualTrips.Include(a => a.StopTimes).SingleOrDefaultAsync(a => a.PlannedTripId == trip.Id, ct);
        var arrUtc = arr.HasValue ? NetworkAdminRules.ToUtc(arr.Value) : (DateTime?)null;
        var depUtc = dep.HasValue ? NetworkAdminRules.ToUtc(dep.Value) : (DateTime?)null;
        if (arrUtc.HasValue && depUtc.HasValue && depUtc.Value < arrUtc.Value)
            throw new NetworkAdminException("INVALID_STOP_TIME", StatusCodes.Status400BadRequest, "actualDepartureAt cannot precede actualArrivalAt.");
        if (actual is null)
        {
            actual = new ActualTrip { OperatorId = opId, PlannedTripId = trip.Id, Status = trip.Status == "PLANNED" ? "READY" : trip.Status, ActualStartAt = arrUtc, Source = "MANUAL", Reason = reason?.Trim(), CreatedAt = now, UpdatedAt = now };
            db.ActualTrips.Add(actual);
        }
        else { actual.Status = trip.Status == "PLANNED" ? "READY" : trip.Status; actual.Reason = reason?.Trim() ?? actual.Reason; actual.UpdatedAt = now; }
        var seq = trip.StopTimes.FirstOrDefault(s => s.StationId == stationId)?.SequenceNo ?? (short)(actual.StopTimes.Count + 1);
        var stop = actual.StopTimes.SingleOrDefault(s => s.StationId == stationId);
        if (stop is null)
            actual.StopTimes.Add(new ActualStopTime { OperatorId = opId, ActualTripId = actual.Id, StationId = stationId, SequenceNo = seq, ActualArrivalAt = arrUtc, ActualDepartureAt = depUtc, Source = "MANUAL" });
        else { stop.ActualArrivalAt = arrUtc ?? stop.ActualArrivalAt; stop.ActualDepartureAt = depUtc ?? stop.ActualDepartureAt; }
    }

    private async Task<Guid> InsertAllocationSqlAsync(Guid opId, Guid tripId, Guid trainsetId, DateTime fromUtc, DateTime toUtc, string actorId, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO metro.trip_allocations (id, operator_id, planned_trip_id, trainset_id, allocated_from, allocated_to, status, created_by_user_id)
                VALUES ({id}, {opId}, {tripId}, {trainsetId}, {fromUtc}, {toUtc}, 'ALLOCATED', {actorId})
                """, ct);
            return id;
        }
        catch (Exception ex)
        {
            throw NetworkAdminHelper.MapPostgresException(ex, "TRAINSET_ALLOCATION_CONFLICT", "Trainset allocation conflicted.",
                "TRAINSET_ALLOCATION_OVERLAP", "Trainset is already allocated to an overlapping trip window (BR-053).");
        }
    }

    private async Task<Dictionary<Guid, AdminTripAllocationResponse>> LoadActiveAllocationsAsync(Guid opId, Guid[] tripIds, CancellationToken ct)
    {
        if (tripIds.Length == 0) return [];
        var rows = await db.Database.SqlQuery<AllocationRow>($"""
            SELECT a.planned_trip_id AS "PlannedTripId", a.id AS "Id", a.trainset_id AS "TrainsetId", t.code AS "TrainsetCode",
                   a.allocated_from AS "AllocatedFrom", a.allocated_to AS "AllocatedTo", a.status AS "Status", a.created_at AS "CreatedAt"
            FROM metro.trip_allocations a JOIN metro.trainsets t ON t.operator_id = a.operator_id AND t.id = a.trainset_id
            WHERE a.operator_id = {opId} AND a.status = 'ALLOCATED' AND a.planned_trip_id = ANY({tripIds})
            ORDER BY a.created_at DESC
            """).ToListAsync(ct);
        return rows.GroupBy(r => r.PlannedTripId).ToDictionary(g => g.Key, g =>
        { var r = g.First(); return new AdminTripAllocationResponse(r.Id, r.TrainsetId, r.TrainsetCode, r.AllocatedFrom, r.AllocatedTo, r.Status, r.CreatedAt); });
    }

    private Task<bool> LoadProfileExistsAsync(Guid opId, Guid profileId, CancellationToken ct) =>
        db.Database.SqlQuery<bool>($"""SELECT EXISTS (SELECT 1 FROM metro.capacity_profiles WHERE operator_id = {opId} AND id = {profileId}) AS "Value" """).SingleAsync(ct);

    private Task<TrainsetBrief?> LoadTrainsetBriefAsync(Guid opId, Guid trainsetId, CancellationToken ct) =>
        db.Database.SqlQuery<TrainsetBrief>($"""SELECT id AS "Id", code AS "Code", status AS "Status" FROM metro.trainsets WHERE operator_id = {opId} AND id = {trainsetId}""").SingleOrDefaultAsync(ct);

    private async Task SaveMappedAsync(string dupCode, string dupMsg, CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) { throw NetworkAdminHelper.MapPostgresException(ex, dupCode, dupMsg); }
    }

    private static object TripSnapshot(PlannedTrip t) => new { t.Id, t.TripCode, t.ServiceDate, t.Direction, t.Status, t.BoardingStationId, t.BoardingStatus, t.LoadProfileId, t.CancellationReason };
    private static AdminTripResponse ToTripResponse(PlannedTrip t, AdminTripAllocationResponse? alloc) => new(
        t.Id, t.ScheduleVersionId, t.LineId, t.TripCode, t.ServiceDate, t.Direction, t.PlannedStartAt, t.PlannedEndAt,
        t.Status, t.LoadProfileId, t.BoardingStationId, t.BoardingStatus, t.CancellationReason, t.CreatedAt, t.UpdatedAt,
        t.StopTimes.OrderBy(s => s.SequenceNo).Select(s => new AdminTripStopResponse(s.Id, s.StationId, s.Station?.Code ?? string.Empty, s.Station?.Name ?? string.Empty, s.SequenceNo, s.PlannedArrivalAt, s.PlannedDepartureAt, s.DwellSeconds)).ToArray(), alloc);

    private sealed record TrainsetBrief(Guid Id, string Code, string Status);
    private sealed record AllocationRow(Guid PlannedTripId, Guid Id, Guid TrainsetId, string TrainsetCode, DateTime AllocatedFrom, DateTime AllocatedTo, string Status, DateTime CreatedAt);
}
