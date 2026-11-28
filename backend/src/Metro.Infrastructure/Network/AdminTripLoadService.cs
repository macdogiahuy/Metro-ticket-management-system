using Metro.Application.Abstractions.Catalog;
using Metro.Application.Abstractions.MultiTenancy;
using Metro.Application.Abstractions.Network;
using Metro.Application.Network;
using Metro.Infrastructure.Persistence.Context;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Metro.Infrastructure.Network;

public sealed class AdminTripLoadService(
    ApplicationDbContext db,
    IOperatorContext operatorContext,
    ICatalogCache catalogCache) : IAdminTripLoadService
{
    public async Task<IReadOnlyList<AdminTrainsetResponse>> ListTrainsetsAsync(string adminUserId, CancellationToken ct)
    {
        var opId = await NetworkAdminHelper.ResolveAdminOperatorAsync(db, operatorContext, adminUserId, ct);
        var ts = await db.Database.SqlQuery<TrainsetRow>($"""SELECT id AS "Id", code AS "Code", depot_code AS "DepotCode", status AS "Status", updated_at AS "UpdatedAt" FROM metro.trainsets WHERE operator_id = {opId} ORDER BY code""").ToListAsync(ct);
        var cars = await db.Database.SqlQuery<CarRow>($"""SELECT id AS "Id", trainset_id AS "TrainsetId", code AS "Code", sequence_no AS "SequenceNo", car_type AS "CarType", max_load_kg AS "MaxLoadKg" FROM metro.cars WHERE operator_id = {opId} ORDER BY trainset_id, sequence_no""").ToListAsync(ct);
        var byTs = cars.GroupBy(c => c.TrainsetId).ToDictionary(g => g.Key, g => g.Select(c => new AdminCarResponse(c.Id, c.Code, c.SequenceNo, c.CarType, c.MaxLoadKg)).ToArray());
        return ts.Select(t => { var tc = byTs.GetValueOrDefault(t.Id) ?? []; return new AdminTrainsetResponse(t.Id, t.Code, t.DepotCode, t.Status, tc.Sum(c => c.MaxLoadKg ?? 0m), t.UpdatedAt, tc); }).ToArray();
    }

    public async Task<AdminTrainsetResponse> CreateTrainsetAsync(CreateTrainsetRequest req, string adminUserId, CancellationToken ct)
    {
        var opId = await NetworkAdminHelper.ResolveAdminOperatorAsync(db, operatorContext, adminUserId, ct);
        var code = NetworkAdminRules.RequireText(req.Code, 40, "TRAINSET_CODE_REQUIRED", "Trainset code is required (max 40 chars).");
        var status = string.IsNullOrWhiteSpace(req.Status) ? "AVAILABLE" : NetworkAdminRules.NormalizeTrainsetStatus(req.Status);
        var cars = ValidateCars(req.Cars);
        var tsId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await ExecuteMappedAsync("TRAINSET_OR_CAR_DUPLICATE", "Trainset or car code/sequence already exists.", async () =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""INSERT INTO metro.trainsets (id, operator_id, code, depot_code, status, status_changed_at, created_at, updated_at) VALUES ({tsId}, {opId}, {code}, {req.DepotCode?.Trim()}, {status}, {now}, {now}, {now})""", ct);
            foreach (var c in cars)
                await db.Database.ExecuteSqlInterpolatedAsync($"""INSERT INTO metro.cars (id, operator_id, trainset_id, code, sequence_no, car_type, max_load_kg, created_at) VALUES ({Guid.NewGuid()}, {opId}, {tsId}, {c.Code}, {c.SequenceNo}, {c.CarType}, {c.MaxLoadKg}, {now})""", ct);
            NetworkAdminHelper.AddAuditLog(db, opId, adminUserId, "CREATE_TRAINSET", "TRAINSET", tsId, ["code", "depot_code", "status", "cars"], null, new { tsId, code, status, cars }, req.Reason);
            await db.SaveChangesAsync(ct);
        }, ct);
        return (await ListTrainsetsAsync(adminUserId, ct)).Single(t => t.Id == tsId);
    }

    public async Task<AdminTrainsetResponse> UpdateTrainsetAsync(Guid trainsetId, UpdateTrainsetRequest req, string adminUserId, CancellationToken ct)
    {
        var opId = await NetworkAdminHelper.ResolveAdminOperatorAsync(db, operatorContext, adminUserId, ct);
        var existing = await db.Database.SqlQuery<TrainsetRow>($"""SELECT id AS "Id", code AS "Code", depot_code AS "DepotCode", status AS "Status", updated_at AS "UpdatedAt" FROM metro.trainsets WHERE operator_id = {opId} AND id = {trainsetId}""").SingleOrDefaultAsync(ct)
            ?? throw new NetworkAdminException("TRAINSET_NOT_FOUND", StatusCodes.Status404NotFound, "Trainset was not found.");
        var status = NetworkAdminRules.NormalizeTrainsetStatus(req.Status);
        var cars = req.Cars is not null ? ValidateCars(req.Cars) : null;
        var now = DateTime.UtcNow;
        // BR-056: updating trainset/car definitions never retroactively alters assigned trip capacity_profiles.
        await ExecuteMappedAsync("TRAINSET_OR_CAR_DUPLICATE", "Trainset or car code/sequence conflicted.", async () =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""UPDATE metro.trainsets SET depot_code = {req.DepotCode?.Trim()}, status = {status}, status_changed_at = {now}, updated_at = {now} WHERE operator_id = {opId} AND id = {trainsetId}""", ct);
            if (cars is not null)
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM metro.cars WHERE operator_id = {opId} AND trainset_id = {trainsetId}", ct);
                foreach (var c in cars)
                    await db.Database.ExecuteSqlInterpolatedAsync($"""INSERT INTO metro.cars (id, operator_id, trainset_id, code, sequence_no, car_type, max_load_kg, created_at) VALUES ({Guid.NewGuid()}, {opId}, {trainsetId}, {c.Code}, {c.SequenceNo}, {c.CarType}, {c.MaxLoadKg}, {now})""", ct);
            }
            NetworkAdminHelper.AddAuditLog(db, opId, adminUserId, "UPDATE_TRAINSET", "TRAINSET", trainsetId, ["depot_code", "status", "cars"], existing, new { trainsetId, req.DepotCode, status, cars }, req.Reason);
            await db.SaveChangesAsync(ct);
        }, ct);
        return (await ListTrainsetsAsync(adminUserId, ct)).Single(t => t.Id == trainsetId);
    }

    public async Task<IReadOnlyList<AdminLoadProfileResponse>> ListLoadProfilesAsync(Guid? trainsetId, string? status, string adminUserId, CancellationToken ct)
    {
        var opId = await NetworkAdminHelper.ResolveAdminOperatorAsync(db, operatorContext, adminUserId, ct);
        var normalized = string.IsNullOrWhiteSpace(status) ? null : status.Trim().ToUpperInvariant();
        return await db.Database.SqlQuery<AdminLoadProfileResponse>($"""
            SELECT id AS "Id", code AS "Code", version AS "Version", line_id AS "LineId", trainset_id AS "TrainsetId",
                   max_load_kg AS "MaxLoadKg", status AS "Status", effective_from AS "EffectiveFrom", effective_to AS "EffectiveTo",
                   created_by_user_id AS "CreatedByUserId", approved_by_user_id AS "ApprovedByUserId", approved_at AS "ApprovedAt",
                   created_at AS "CreatedAt", updated_at AS "UpdatedAt"
            FROM metro.capacity_profiles
            WHERE operator_id = {opId} AND ({trainsetId}::uuid IS NULL OR trainset_id = {trainsetId}) AND ({normalized}::text IS NULL OR status = {normalized})
            ORDER BY code, version DESC
            """).ToListAsync(ct);
    }

    public async Task<AdminLoadProfileResponse> CreateLoadProfileAsync(CreateLoadProfileRequest req, string adminUserId, CancellationToken ct)
    {
        var opId = await NetworkAdminHelper.ResolveAdminOperatorAsync(db, operatorContext, adminUserId, ct);
        var code = NetworkAdminRules.RequireText(req.Code, 60, "LOAD_PROFILE_CODE_REQUIRED", "Load profile code is required (max 60 chars).");
        if (req.LineId is null && req.TrainsetId is null)
            throw new NetworkAdminException("LOAD_PROFILE_SCOPE_REQUIRED", StatusCodes.Status400BadRequest, "At least one of lineId or trainsetId is required.");
        var maxKg = NetworkAdminRules.ValidateMaxLoadKg(req.MaxLoadKg);
        var (fromUtc, toUtc) = NetworkAdminRules.ValidateEffectiveWindow(req.EffectiveFrom, req.EffectiveTo);
        if (req.TrainsetId.HasValue) await ValidateTrainsetCarsLimitAsync(opId, req.TrainsetId.Value, maxKg, ct);

        var nextVer = await db.Database.SqlQuery<int>($"""SELECT COALESCE(MAX(version), 0)::int + 1 AS "Value" FROM metro.capacity_profiles WHERE operator_id = {opId} AND code = {code}""").SingleAsync(ct);
        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await ExecuteMappedAsync("LOAD_PROFILE_DUPLICATE", $"Load profile '{code}' v{nextVer} already exists.", async () =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO metro.capacity_profiles (id, operator_id, code, version, line_id, trainset_id, seated_capacity, standing_capacity, design_factor, max_load_kg, status, effective_from, effective_to, created_by_user_id, created_at, updated_at)
                VALUES ({id}, {opId}, {code}, {nextVer}, {req.LineId}, {req.TrainsetId}, 0, 0, 1, {maxKg}, 'DRAFT', {fromUtc}, {toUtc}, {adminUserId}, {now}, {now})
                """, ct);
            NetworkAdminHelper.AddAuditLog(db, opId, adminUserId, "CREATE_LOAD_PROFILE", "CAPACITY_PROFILE", id, ["code", "version", "line_id", "trainset_id", "max_load_kg", "status"], null, new { id, code, nextVer, maxKg }, req.Reason);
            await db.SaveChangesAsync(ct);
        }, ct);
        return await GetProfileByIdAsync(opId, id, ct);
    }

    public Task<AdminLoadProfileResponse> ApproveLoadProfileAsync(Guid profileId, LoadProfileDecisionRequest req, string adminUserId, CancellationToken ct) =>
        TransitionProfileAsync(profileId, approve: true, req.Reason, adminUserId, ct);

    public Task<AdminLoadProfileResponse> PublishLoadProfileAsync(Guid profileId, LoadProfileDecisionRequest req, string adminUserId, CancellationToken ct) =>
        TransitionProfileAsync(profileId, approve: false, req.Reason, adminUserId, ct);

    private async Task<AdminLoadProfileResponse> TransitionProfileAsync(Guid profileId, bool approve, string? reason, string adminUserId, CancellationToken ct)
    {
        var opId = await NetworkAdminHelper.ResolveAdminOperatorAsync(db, operatorContext, adminUserId, ct);
        var p = await GetProfileByIdAsync(opId, profileId, ct);
        if (approve && p.Status is not ("DRAFT" or "PENDING_APPROVAL"))
            throw new NetworkAdminException("INVALID_LOAD_PROFILE_TRANSITION", StatusCodes.Status409Conflict, $"Cannot approve load profile from '{p.Status}'.");
        if (!approve && p.Status != "APPROVED")
            throw new NetworkAdminException("INVALID_LOAD_PROFILE_TRANSITION", StatusCodes.Status409Conflict, "Load profile must be APPROVED before publishing.");
        if (approve) NetworkAdminRules.EnsureMakerChecker(p.CreatedByUserId, adminUserId, "load profile");
        if (p.MaxLoadKg is null) throw new NetworkAdminException("INVALID_MAX_LOAD_KG", StatusCodes.Status400BadRequest, "Load profile has no max_load_kg.");
        if (p.TrainsetId.HasValue) await ValidateTrainsetCarsLimitAsync(opId, p.TrainsetId.Value, p.MaxLoadKg.Value, ct);

        var now = DateTime.UtcNow;
        var nextStatus = approve ? "APPROVED" : "PUBLISHED";
        await ExecuteMappedAsync("LOAD_PROFILE_CONFLICT", "Load profile state update conflicted.", async () =>
        {
            if (approve)
                await db.Database.ExecuteSqlInterpolatedAsync($"""UPDATE metro.capacity_profiles SET status = 'APPROVED', approved_by_user_id = {adminUserId}, approved_at = {now}, updated_at = {now} WHERE operator_id = {opId} AND id = {profileId}""", ct);
            else
                await db.Database.ExecuteSqlInterpolatedAsync($"""UPDATE metro.capacity_profiles SET status = 'PUBLISHED', updated_at = {now} WHERE operator_id = {opId} AND id = {profileId}""", ct);
            NetworkAdminHelper.AddAuditLog(db, opId, adminUserId, approve ? "APPROVE_LOAD_PROFILE" : "PUBLISH_LOAD_PROFILE", "CAPACITY_PROFILE", profileId, ["status"], p, new { profileId, Status = nextStatus }, reason);
            await db.SaveChangesAsync(ct);
        }, ct);
        return await GetProfileByIdAsync(opId, profileId, ct);
    }

    public async Task<IReadOnlyList<AdminTripLoadStatusResponse>> GetTripLoadStatusesAsync(Guid? tripId, Guid? lineId, DateOnly? serviceDate, string adminUserId, CancellationToken ct)
    {
        var opId = await NetworkAdminHelper.ResolveAdminOperatorAsync(db, operatorContext, adminUserId, ct);
        return await db.Database.SqlQuery<AdminTripLoadStatusResponse>($"""
            SELECT v.planned_trip_id AS "PlannedTripId", v.trip_code AS "TripCode", v.service_date AS "ServiceDate",
                   v.boarding_status AS "BoardingStatus", v.observed_load_kg AS "ObservedLoadKg",
                   v.observed_at AS "ObservedAt", v.max_load_kg AS "MaxLoadKg", v.load_status AS "LoadStatus"
            FROM metro.v_trip_load_status v
            JOIN metro.planned_trips pt ON pt.operator_id = v.operator_id AND pt.id = v.planned_trip_id
            WHERE v.operator_id = {opId} AND ({tripId}::uuid IS NULL OR v.planned_trip_id = {tripId})
              AND ({lineId}::uuid IS NULL OR pt.line_id = {lineId}) AND ({serviceDate}::date IS NULL OR v.service_date = {serviceDate})
            ORDER BY v.service_date DESC, pt.planned_start_at ASC LIMIT 200
            """).ToListAsync(ct);
    }

    public async Task<AdminTripLoadObservationResponse> RecordLoadObservationAsync(RecordTripLoadObservationRequest req, string adminUserId, CancellationToken ct)
    {
        var opId = await NetworkAdminHelper.ResolveAdminOperatorAsync(db, operatorContext, adminUserId, ct);
        var kg = NetworkAdminRules.ValidateObservedLoadKg(req.ObservedLoadKg);
        var now = DateTime.UtcNow;
        var observedAt = req.ObservedAt.HasValue ? NetworkAdminRules.ValidateObservedAt(req.ObservedAt.Value, now) : now;
        var trip = await db.PlannedTrips.AsNoTracking().SingleOrDefaultAsync(t => t.Id == req.PlannedTripId, ct)
            ?? throw new NetworkAdminException("TRIP_NOT_FOUND", StatusCodes.Status404NotFound, "Planned trip was not found.");
        if (trip.Status is "COMPLETED" or "CANCELLED")
            throw new NetworkAdminException("TRIP_ALREADY_ENDED", StatusCodes.Status409Conflict, "Cannot record load on a completed or cancelled trip.");

        decimal? maxKg = trip.LoadProfileId.HasValue
            ? await db.Database.SqlQuery<decimal?>($"""SELECT max_load_kg AS "Value" FROM metro.capacity_profiles WHERE operator_id = {opId} AND id = {trip.LoadProfileId}""").SingleOrDefaultAsync(ct)
            : null;
        var id = Guid.NewGuid();
        var note = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim();
        await ExecuteMappedAsync("LOAD_OBSERVATION_CONFLICT", "Load observation conflict.", async () =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO metro.load_observations (id, operator_id, planned_trip_id, capacity_profile_id, observed_load_kg, source, recorded_by_user_id, observed_at, recorded_at, note)
                VALUES ({id}, {opId}, {trip.Id}, {trip.LoadProfileId}, {kg}, 'MANUAL', {adminUserId}, {observedAt}, {now}, {note})
                """, ct);
            NetworkAdminHelper.AddAuditLog(db, opId, adminUserId, "RECORD_TRIP_LOAD", "PLANNED_TRIP", trip.Id, ["observed_load_kg", "observed_at", "capacity_profile_id"], null, new { id, kg, observedAt, maxKg }, note);
            await db.SaveChangesAsync(ct);
        }, ct);
        await catalogCache.InvalidateOperatorAsync(opId, ct);
        return new AdminTripLoadObservationResponse(id, trip.Id, trip.LoadProfileId, kg, maxKg, NetworkAdminRules.ComputeLoadStatus(kg, maxKg), "MANUAL", adminUserId, observedAt, now, note);
    }

    public async Task<AdminTripEtaUpdateResponse> RecordEtaUpdateAsync(RecordTripEtaUpdateRequest req, string adminUserId, CancellationToken ct)
    {
        var opId = await NetworkAdminHelper.ResolveAdminOperatorAsync(db, operatorContext, adminUserId, ct);
        var (arrUtc, depUtc) = NetworkAdminRules.ValidateManualEta(req.ExpectedArrivalAt, req.ExpectedDepartureAt);
        var trip = await db.PlannedTrips.AsNoTracking().Include(t => t.StopTimes).SingleOrDefaultAsync(t => t.Id == req.PlannedTripId, ct)
            ?? throw new NetworkAdminException("TRIP_NOT_FOUND", StatusCodes.Status404NotFound, "Planned trip was not found.");
        if (trip.Status is "COMPLETED" or "CANCELLED")
            throw new NetworkAdminException("TRIP_ALREADY_ENDED", StatusCodes.Status409Conflict, "Cannot record ETA on a completed or cancelled trip.");
        if (!await db.Stations.AsNoTracking().AnyAsync(s => s.Id == req.StationId, ct))
            throw new NetworkAdminException("STATION_NOT_FOUND", StatusCodes.Status404NotFound, "Station was not found.");
        if (trip.StopTimes.Count > 0 && !trip.StopTimes.Any(s => s.StationId == req.StationId))
            throw new NetworkAdminException("ETA_STATION_NOT_ON_TRIP", StatusCodes.Status400BadRequest, "Station is not part of this trip's stop list.");

        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var note = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim();
        await ExecuteMappedAsync("ETA_UPDATE_CONFLICT", "ETA update conflict.", async () =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO metro.trip_eta_updates (id, operator_id, planned_trip_id, station_id, expected_arrival_at, expected_departure_at, source, recorded_by_user_id, recorded_at, note)
                VALUES ({id}, {opId}, {trip.Id}, {req.StationId}, {arrUtc}, {depUtc}, 'MANUAL', {adminUserId}, {now}, {note})
                """, ct);
            NetworkAdminHelper.AddAuditLog(db, opId, adminUserId, "RECORD_TRIP_ETA", "PLANNED_TRIP", trip.Id, ["station_id", "expected_arrival_at", "expected_departure_at"], null, new { id, req.StationId, arrUtc, depUtc }, note);
            await db.SaveChangesAsync(ct);
        }, ct);
        await catalogCache.InvalidateOperatorAsync(opId, ct);
        return new AdminTripEtaUpdateResponse(id, trip.Id, req.StationId, arrUtc, depUtc, "MANUAL", adminUserId, now, note);
    }

    private async Task ValidateTrainsetCarsLimitAsync(Guid opId, Guid trainsetId, decimal profileMaxKg, CancellationToken ct)
    {
        var s = await db.Database.SqlQuery<CarLimitSummary>($"""SELECT count(*) FILTER (WHERE max_load_kg IS NULL)::int AS "MissingCount", COALESCE(sum(max_load_kg), 0)::numeric AS "TotalKg" FROM metro.cars WHERE operator_id = {opId} AND trainset_id = {trainsetId}""").SingleAsync(ct);
        if (s.MissingCount > 0) throw new NetworkAdminException("CAR_MAX_LOAD_MISSING", StatusCodes.Status400BadRequest, "Every car in the trainset must declare max_load_kg before creating/publishing a load profile (UC37).");
        if (s.TotalKg > profileMaxKg) throw new NetworkAdminException("LOAD_PROFILE_CAR_LIMIT_EXCEEDED", StatusCodes.Status400BadRequest, $"Sum of car load limits ({s.TotalKg:0.0} kg) exceeds profile max_load_kg ({profileMaxKg:0.0} kg).");
    }

    private async Task<AdminLoadProfileResponse> GetProfileByIdAsync(Guid opId, Guid id, CancellationToken ct) =>
        await db.Database.SqlQuery<AdminLoadProfileResponse>($"""
            SELECT id AS "Id", code AS "Code", version AS "Version", line_id AS "LineId", trainset_id AS "TrainsetId",
                   max_load_kg AS "MaxLoadKg", status AS "Status", effective_from AS "EffectiveFrom", effective_to AS "EffectiveTo",
                   created_by_user_id AS "CreatedByUserId", approved_by_user_id AS "ApprovedByUserId", approved_at AS "ApprovedAt",
                   created_at AS "CreatedAt", updated_at AS "UpdatedAt"
            FROM metro.capacity_profiles WHERE operator_id = {opId} AND id = {id}
            """).SingleOrDefaultAsync(ct)
        ?? throw new NetworkAdminException("LOAD_PROFILE_NOT_FOUND", StatusCodes.Status404NotFound, "Load profile was not found.");

    private static TrainsetCarRequest[] ValidateCars(IReadOnlyList<TrainsetCarRequest>? cars)
    {
        if (cars is null || cars.Count == 0) throw new NetworkAdminException("TRAINSET_CARS_REQUIRED", StatusCodes.Status400BadRequest, "At least one car with maxLoadKg is required.");
        var ordered = cars.OrderBy(c => c.SequenceNo).ToArray();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < ordered.Length; i++)
        {
            var code = NetworkAdminRules.RequireText(ordered[i].Code, 40, "CAR_CODE_REQUIRED", "Car code is required (max 40 chars).");
            if (ordered[i].SequenceNo != i + 1) throw new NetworkAdminException("INVALID_CAR_SEQUENCE", StatusCodes.Status400BadRequest, "Car sequence numbers must be contiguous starting from 1.");
            if (!seen.Add(code)) throw new NetworkAdminException("DUPLICATE_CAR_CODE", StatusCodes.Status400BadRequest, $"Duplicate car code '{code}'.");
            ordered[i] = new TrainsetCarRequest(code, ordered[i].SequenceNo, ordered[i].CarType?.Trim(), NetworkAdminRules.ValidateMaxLoadKg(ordered[i].MaxLoadKg));
        }
        return ordered;
    }

    private Task ExecuteMappedAsync(string dupCode, string dupMsg, Func<Task> action, CancellationToken ct) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            try { await action(); await tx.CommitAsync(ct); }
            catch (NetworkAdminException) { throw; }
            catch (Exception ex) { throw NetworkAdminHelper.MapPostgresException(ex, dupCode, dupMsg); }
        });

    private sealed record TrainsetRow(Guid Id, string Code, string? DepotCode, string Status, DateTime UpdatedAt);
    private sealed record CarRow(Guid Id, Guid TrainsetId, string Code, short SequenceNo, string? CarType, decimal? MaxLoadKg);
    private sealed record CarLimitSummary(int MissingCount, decimal TotalKg);
}
