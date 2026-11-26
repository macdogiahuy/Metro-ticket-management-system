using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Metro.Application.Abstractions.MultiTenancy;
using Metro.Application.Abstractions.Operations;
using Metro.Application.Operations;
using Metro.Application.Ticketing;
using Metro.Domain.Entities.Identity;
using Metro.Domain.Entities.Network;
using Metro.Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace Metro.Infrastructure.Operations;

public sealed class OperationsService(ApplicationDbContext db, IOperatorContext scope, TimeProvider time) : IOperationsService
{
    public Task<OperationReceipt> UpdateEtaAsync(string actor, string operatorCode, Guid tripId,
        UpdateEtaRequest request, string key, CancellationToken ct) =>
        WriteAsync(actor, operatorCode, "ETA_RECORDED", key, new { tripId, request }, request.Reason, async op =>
        {
            var trip = await TripAsync(op, tripId, ct);
            RequireLive(trip);
            OperationsRules.ValidateEta(request, trip.ServiceDate);
            if (!await db.PlannedStopTimes.AnyAsync(s => s.PlannedTripId == tripId && s.StationId == request.StationId, ct))
                throw Error("ETA_STATION_NOT_ON_TRIP", 404);
            var id = Guid.NewGuid();
            var now = time.GetUtcNow();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO metro.trip_eta_updates(id,operator_id,planned_trip_id,station_id,expected_arrival_at,
                    expected_departure_at,recorded_by_user_id,recorded_at,note)
                VALUES({id},{op},{tripId},{request.StationId},{request.ExpectedArrivalAt},{request.ExpectedDepartureAt},{actor},{now},{request.Reason.Trim()})
                """, ct);
            return new(id, "RECORDED", now);
        }, ct);

    public Task<OperationReceipt> RecordLoadAsync(string actor, string operatorCode, Guid tripId,
        RecordLoadRequest request, string key, CancellationToken ct) =>
        WriteAsync(actor, operatorCode, "LOAD_RECORDED", key, new { tripId, request }, request.Reason, async op =>
        {
            var trip = await TripAsync(op, tripId, ct);
            RequireLive(trip);
            OperationsRules.ValidateLoad(request, time.GetUtcNow(), trip.ServiceDate);
            if (trip.LoadProfileId is null || !await db.Database.SqlQuery<bool>($"""
                SELECT EXISTS(SELECT 1 FROM metro.capacity_profiles WHERE operator_id={op}
                    AND id={trip.LoadProfileId} AND max_load_kg>0) AS "Value"
                """).SingleAsync(ct)) throw Error("TRIP_LOAD_PROFILE_REQUIRED", 409);
            var id = Guid.NewGuid();
            var now = time.GetUtcNow();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO metro.load_observations(id,operator_id,planned_trip_id,capacity_profile_id,observed_load_kg,
                    recorded_by_user_id,observed_at,recorded_at,note)
                VALUES({id},{op},{tripId},{trip.LoadProfileId},{request.ObservedLoadKg},{actor},{request.ObservedAt},{now},{request.Reason.Trim()})
                """, ct);
            return new(id, "RECORDED", now);
        }, ct);

    public Task<OperationReceipt> UpdateTripStateAsync(string actor, string operatorCode, Guid tripId,
        UpdateTripStateRequest request, string key, CancellationToken ct) =>
        WriteAsync(actor, operatorCode, "TRIP_STATE_CHANGED", key, new { tripId, request }, request.Reason, async op =>
        {
            var trip = await TripAsync(op, tripId, ct);
            var target = OperationsRules.ValidateTripTransition(trip.Status, request.Status);
            if (target is not ("COMPLETED" or "CANCELLED") && await db.Database.SqlQuery<bool>($"""
                SELECT EXISTS(SELECT 1 FROM metro.service_disruptions WHERE operator_id={op} AND planned_trip_id={tripId}
                    AND status='PUBLISHED' AND CASE WHEN effect='DELAYED' THEN {target}<>'DELAYED' ELSE {target}<>'DISRUPTED' END) AS "Value"
                """).SingleAsync(ct)) throw Error("ACTIVE_DISRUPTION_REQUIRES_RECOVERY", 409);
            var boarding = request.BoardingStatus?.Trim().ToUpperInvariant();
            if (boarding is not (null or "OPEN" or "CLOSED")) throw Error("INVALID_BOARDING_STATUS", 400);
            if (target is "COMPLETED" or "CANCELLED" or "DISRUPTED")
            {
                if (boarding == "OPEN") throw Error("TRIP_NOT_BOARDABLE", 409);
                boarding = "CLOSED";
            }
            if (boarding == "OPEN" && (target is not ("IN_SERVICE" or "DELAYED") || trip.BoardingStationId is null ||
                !await db.Database.SqlQuery<bool>($"""
                    SELECT EXISTS(SELECT 1 FROM metro.actual_trips a
                        JOIN metro.actual_stop_times s ON s.operator_id=a.operator_id AND s.actual_trip_id=a.id
                        WHERE a.operator_id={op} AND a.planned_trip_id={tripId} AND s.station_id={trip.BoardingStationId}
                        AND s.actual_arrival_at IS NOT NULL AND s.actual_departure_at IS NULL
                        AND NOT EXISTS(SELECT 1 FROM metro.actual_stop_times later WHERE later.operator_id=a.operator_id
                            AND later.actual_trip_id=a.id AND later.sequence_no>s.sequence_no AND later.actual_arrival_at IS NOT NULL)) AS "Value"
                    """).SingleAsync(ct))) throw Error("BOARDING_CONTEXT_REQUIRED", 409);
            trip.Status = target;
            trip.BoardingStatus = boarding ?? trip.BoardingStatus;
            trip.UpdatedAt = time.GetUtcNow().UtcDateTime;
            if (target == "CANCELLED") trip.CancellationReason = request.Reason.Trim();
            if (target != "PLANNED")
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE metro.actual_trips SET status={target},reason={request.Reason.Trim()},updated_at={time.GetUtcNow()}
                    WHERE operator_id={op} AND planned_trip_id={tripId} AND status NOT IN ('COMPLETED','CANCELLED')
                    """, ct);
            return new(tripId, target, time.GetUtcNow());
        }, ct);

    public Task<OperationReceipt> CreateDisruptionAsync(string actor, string operatorCode,
        CreateDisruptionRequest request, string key, CancellationToken ct) =>
        WriteAsync(actor, operatorCode, "DISRUPTION_CREATED", key, request, request.Title, async op =>
        {
            var effect = OperationsRules.ValidateDisruption(request, time.GetUtcNow());
            if (request.LineId is { } line && !await db.Lines.AnyAsync(l => l.Id == line, ct) ||
                request.StationId is { } station && !await db.Stations.AnyAsync(s => s.Id == station, ct))
                throw Error("DISRUPTION_TARGET_NOT_FOUND", 404);
            if (request.PlannedTripId is { } trip) RequireLive(await TripAsync(op, trip, ct));
            var id = Guid.NewGuid();
            var now = time.GetUtcNow();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO metro.service_disruptions(id,operator_id,line_id,station_id,planned_trip_id,effect,title,description,
                    occurred_at,created_by_user_id,updated_by_user_id,updated_at)
                VALUES({id},{op},{request.LineId},{request.StationId},{request.PlannedTripId},{effect},{request.Title.Trim()},
                    {request.Description.Trim()},{request.OccurredAt},{actor},{actor},{now})
                """, ct);
            return new(id, "DRAFT", now, false);
        }, ct);

    public Task<OperationReceipt> DecideDisruptionAsync(string actor, string operatorCode, Guid id,
        DisruptionDecisionRequest request, bool resolve, string key, CancellationToken ct) =>
        WriteAsync(actor, operatorCode, resolve ? "DISRUPTION_RESOLVED" : "DISRUPTION_PUBLISHED", key,
            new { id, request }, request.Reason, async op =>
        {
            if (!request.Confirmed) throw Error("DISRUPTION_CONFIRMATION_REQUIRED", 400);
            var state = await db.Database.SqlQuery<DisruptionState>($"""
                SELECT id AS "Id",line_id AS "LineId",station_id AS "StationId",planned_trip_id AS "PlannedTripId",
                    effect AS "Effect",status AS "Status",title AS "Title",description AS "Description",occurred_at AS "OccurredAt",
                    previous_status AS "PreviousStatus",applied_status AS "AppliedStatus",previous_boarding_status AS "PreviousBoardingStatus"
                FROM metro.service_disruptions WHERE operator_id={op} AND id={id}
                """).SingleOrDefaultAsync(ct) ?? throw Error("DISRUPTION_NOT_FOUND", 404);
            if (state.Status != (resolve ? "PUBLISHED" : "DRAFT")) throw Error("INVALID_DISRUPTION_TRANSITION", 409);
            if (!resolve && await db.Database.SqlQuery<bool>($"""
                SELECT EXISTS(SELECT 1 FROM metro.service_disruptions WHERE operator_id={op} AND status='PUBLISHED'
                    AND (line_id={state.LineId} OR station_id={state.StationId} OR planned_trip_id={state.PlannedTripId})) AS "Value"
                """).SingleAsync(ct)) throw Error("DISRUPTION_ALREADY_ACTIVE", 409);
            var (lineId, previous, applied, oldBoarding) = await ApplyEffectAsync(op, state, resolve, ct);
            var now = time.GetUtcNow();
            if (resolve)
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE metro.service_disruptions SET status='RESOLVED',resolved_at={now},updated_by_user_id={actor},updated_at={now}
                    WHERE operator_id={op} AND id={id};
                    UPDATE metro.incidents SET status='RECOVERED',recovered_at={now},recovery_summary={request.Reason.Trim()},updated_at={now}
                    WHERE operator_id={op} AND id={id};
                    UPDATE metro.service_notices SET status='EXPIRED',
                        valid_to=CASE WHEN valid_from<{now} THEN {now} ELSE NULL END,updated_at={now}
                    WHERE operator_id={op} AND incident_id={id} AND status='PUBLISHED'
                    """, ct);
            }
            else
            {
                var code = $"DIS-{id:N}";
                var type = state.Effect == "DELAYED" ? "SERVICE_DELAY" : "SERVICE_SUSPENSION";
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE metro.service_disruptions SET status='PUBLISHED',published_at={now},previous_status={previous},
                        applied_status={applied},previous_boarding_status={oldBoarding},updated_by_user_id={actor},updated_at={now}
                    WHERE operator_id={op} AND id={id};
                    INSERT INTO metro.incidents(id,operator_id,code,incident_type,severity,status,line_id,station_id,occurred_at,
                        summary,impact_summary,reported_by_user_id)
                    VALUES({id},{op},{code},{type},'MEDIUM','IN_RESPONSE',{lineId},{state.StationId},{state.OccurredAt},
                        {state.Title},{state.Description},{actor});
                    INSERT INTO metro.service_notices(operator_id,incident_id,code,audience,title,content,status,valid_from,
                        created_by_user_id,published_by_user_id,published_at,decision_reason)
                    VALUES({op},{id},{code},'PUBLIC',{state.Title},{state.Description},'PUBLISHED',{now},{actor},{actor},{now},{request.Reason.Trim()})
                    """, ct);
                if (state.PlannedTripId is { } tripId)
                    await db.Database.ExecuteSqlInterpolatedAsync($"""
                        INSERT INTO metro.incident_trips(operator_id,incident_id,planned_trip_id,impact_type)
                        VALUES({op},{id},{tripId},{state.Effect})
                        """, ct);
            }
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO metro.incident_timeline(operator_id,incident_id,event_at,actor_user_id,action,notes)
                VALUES({op},{id},{now},{actor},{(resolve ? "SERVICE_RECOVERED" : "SERVICE_PUBLISHED")},{request.Reason.Trim()})
                """, ct);
            await QueueAffectedAsync(op, state, resolve, ct);
            return new(id, resolve ? "RESOLVED" : "PUBLISHED", now);
        }, ct);

    private async Task<(Guid? Line, string Previous, string Applied, string? Boarding)> ApplyEffectAsync(
        Guid op, DisruptionState state, bool resolve, CancellationToken ct)
    {
        if (state.LineId is { } lineId)
        {
            var line = await db.Lines.SingleAsync(l => l.Id == lineId, ct);
            var previous = line.Status;
            var applied = state.Effect == "SUSPENDED" ? "SUSPENDED" : previous;
            line.Status = resolve ? (line.Status == state.AppliedStatus ? state.PreviousStatus! : line.Status) : applied;
            line.UpdatedAt = time.GetUtcNow().UtcDateTime;
            return (lineId, previous, applied, null);
        }
        if (state.StationId is { } stationId)
        {
            var station = await db.Stations.SingleAsync(s => s.Id == stationId, ct);
            var previous = station.Status;
            station.Status = resolve ? (station.Status == state.AppliedStatus ? state.PreviousStatus! : station.Status) : "CLOSED";
            station.UpdatedAt = time.GetUtcNow().UtcDateTime;
            return (null, previous, "CLOSED", null);
        }
        var trip = await TripAsync(op, state.PlannedTripId!.Value, ct);
        if (!resolve) RequireLive(trip);
        var old = trip.Status;
        var appliedTrip = state.Effect == "DELAYED" ? "DELAYED" : "DISRUPTED";
        var board = trip.BoardingStatus;
        if (!resolve || trip.Status == state.AppliedStatus)
        {
            trip.Status = resolve ? state.PreviousStatus! : appliedTrip;
            // Recovery never invents a boarding event or opens a gate.
            if (state.Effect == "SUSPENDED") trip.BoardingStatus = "CLOSED";
            trip.UpdatedAt = time.GetUtcNow().UtcDateTime;
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE metro.actual_trips SET status={trip.Status},updated_at={time.GetUtcNow()}
                WHERE operator_id={op} AND planned_trip_id={trip.Id} AND status NOT IN ('COMPLETED','CANCELLED')
                """, ct);
        }
        return (trip.LineId, old, appliedTrip, board);
    }

    public async Task<TripLoadResponse> GetLoadAsync(string operatorCode, Guid tripId, CancellationToken ct)
    {
        var op = await ResolvePublicAsync(operatorCode, ct);
        return await db.Database.SqlQuery<TripLoadResponse>($"""
            SELECT v.planned_trip_id AS "PlannedTripId",v.trip_code AS "TripCode",v.service_date AS "ServiceDate",
                v.boarding_status AS "BoardingStatus",v.observed_load_kg AS "ObservedLoadKg",v.observed_at AS "ObservedAt",
                v.max_load_kg AS "MaxLoadKg",v.load_status AS "LoadStatus",last_load.recorded_at AS "RecordedAt",'MANUAL' AS "Source"
            FROM metro.v_trip_load_status v LEFT JOIN LATERAL (
                SELECT recorded_at FROM metro.load_observations o WHERE o.operator_id=v.operator_id AND o.planned_trip_id=v.planned_trip_id
                ORDER BY observed_at DESC,recorded_at DESC,id DESC LIMIT 1) last_load ON true
            WHERE v.operator_id={op} AND v.planned_trip_id={tripId}
            """).SingleOrDefaultAsync(ct) ?? throw Error("TRIP_NOT_FOUND", 404);
    }

    public async Task<TripOperationsHistory> GetHistoryAsync(string actor, string operatorCode, Guid tripId, int page, CancellationToken ct)
    {
        ValidatePage(page);
        var op = await ResolveAdminAsync(actor, operatorCode, ct);
        await TripAsync(op, tripId, ct);
        var skip = (page - 1) * 50;
        var eta = await db.Database.SqlQuery<EtaHistoryItem>($"""
            SELECT id AS "Id",station_id AS "StationId",expected_arrival_at AS "ExpectedArrivalAt",
                expected_departure_at AS "ExpectedDepartureAt",recorded_at AS "RecordedAt",recorded_by_user_id AS "RecordedByUserId"
            FROM metro.trip_eta_updates WHERE operator_id={op} AND planned_trip_id={tripId}
            ORDER BY recorded_at DESC,id DESC LIMIT 50 OFFSET {skip}
            """).ToListAsync(ct);
        var loads = await db.Database.SqlQuery<LoadHistoryItem>($"""
            SELECT id AS "Id",observed_load_kg AS "ObservedLoadKg",observed_at AS "ObservedAt",recorded_at AS "RecordedAt",
                recorded_by_user_id AS "RecordedByUserId" FROM metro.load_observations
            WHERE operator_id={op} AND planned_trip_id={tripId} ORDER BY observed_at DESC,recorded_at DESC,id DESC LIMIT 50 OFFSET {skip}
            """).ToListAsync(ct);
        return new(eta, loads);
    }

    public async Task<IReadOnlyList<DisruptionResponse>> ListDisruptionsAsync(string actor, string operatorCode, int page, CancellationToken ct)
    {
        ValidatePage(page);
        var op = await ResolveAdminAsync(actor, operatorCode, ct);
        var skip = (page - 1) * 50;
        return await db.Database.SqlQuery<DisruptionResponse>($"""
            SELECT id AS "Id",title AS "Title",description AS "Description",effect AS "Effect",status AS "Status",line_id AS "LineId",
                station_id AS "StationId",planned_trip_id AS "PlannedTripId",occurred_at AS "OccurredAt",published_at AS "PublishedAt",resolved_at AS "ResolvedAt"
            FROM metro.service_disruptions WHERE operator_id={op} ORDER BY updated_at DESC,id DESC LIMIT 50 OFFSET {skip}
            """).ToListAsync(ct);
    }

    private Task<OperationReceipt> WriteAsync(string actor, string operatorCode, string operation, string key,
        object payload, string reason, Func<Guid, Task<OperationReceipt>> change, CancellationToken ct) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(async _ =>
        {
            db.ChangeTracker.Clear();
            OperationsRules.ValidateKey(key);
            var normalizedReason = OperationsRules.RequireReason(reason);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            // Match account-administration ordering, then serialize service changes for this operator.
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(73190624011222)", ct);
            var op = await ResolveAdminAsync(actor, operatorCode, ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({op.ToString()},14))", ct);
            var keyHash = Hash(key);
            var payloadHash = Hash(JsonSerializer.Serialize(payload));
            var replay = await db.Database.SqlQuery<Replay>($"""
                SELECT payload_hash AS "PayloadHash",response::text AS "Response" FROM metro.operations_requests
                WHERE operator_id={op} AND actor_user_id={actor} AND operation={operation} AND request_key_hash={keyHash}
                """).SingleOrDefaultAsync(ct);
            if (replay is not null)
            {
                if (replay.PayloadHash != payloadHash) throw Error("IDEMPOTENCY_KEY_CONFLICT", 409);
                return JsonSerializer.Deserialize<OperationReceipt>(replay.Response)!;
            }
            var result = await change(op);
            db.AuditLogs.Add(new AuditLog
            {
                OperatorId = op, ActorUserId = actor, Action = operation, EntityType = "SERVICE_OPERATIONS",
                EntityId = result.Id.ToString(), ChangedFields = ["service_state"], AfterHash = payloadHash,
                Reason = normalizedReason, OccurredAt = time.GetUtcNow().UtcDateTime
            });
            await db.SaveChangesAsync(ct);
            var response = JsonSerializer.Serialize(result);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO metro.operations_requests(operator_id,actor_user_id,operation,request_key_hash,payload_hash,response)
                VALUES({op},{actor},{operation},{keyHash},{payloadHash},CAST({response} AS jsonb));
                INSERT INTO metro.operation_revisions(operator_id,revision) VALUES({op},1)
                ON CONFLICT(operator_id) DO UPDATE SET revision=metro.operation_revisions.revision+1
                """, ct);
            await tx.CommitAsync(ct);
            return result;
        }, ct);

    private async Task<Guid> ResolveAdminAsync(string actor, string operatorCode, CancellationToken ct)
    {
        var op = await ResolvePublicAsync(operatorCode, ct);
        var valid = await (from assignment in db.UserRoles
                           join role in db.Roles on assignment.RoleId equals role.Id
                           join user in db.Users on assignment.UserId equals user.Id
                           where assignment.UserId == actor && role.Name == "Admin" && user.EmailConfirmed
                           select user.Id).AnyAsync(ct);
        if (!valid || !await db.OperatorAccounts.IgnoreQueryFilters().AnyAsync(a => a.OperatorId == op && a.UserId == actor && a.Status == "ACTIVE", ct))
            throw Error("OPERATIONS_FORBIDDEN", 403);
        if (await db.UserProfiles.AnyAsync(p => p.UserId == actor && p.Status != "ACTIVE", ct)) throw Error("OPERATIONS_FORBIDDEN", 403);
        return op;
    }

    private async Task<Guid> ResolvePublicAsync(string code, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 50) throw Error("OPERATOR_CODE_REQUIRED", 400);
        var op = await db.Operators.AsNoTracking().Where(o => o.Code == code && o.Status == "ACTIVE")
            .Select(o => (Guid?)o.Id).SingleOrDefaultAsync(ct) ?? throw Error("OPERATOR_NOT_FOUND", 404);
        scope.CurrentOperatorId = op;
        return op;
    }

    private async Task<PlannedTrip> TripAsync(Guid op, Guid id, CancellationToken ct) =>
        await db.PlannedTrips.SingleOrDefaultAsync(t => t.OperatorId == op && t.Id == id, ct) ?? throw Error("TRIP_NOT_FOUND", 404);

    private Task<int> QueueAffectedAsync(Guid op, DisruptionState state, bool resolve, CancellationToken ct)
    {
        var today = TicketRules.LocalDate(time.GetUtcNow());
        var type = resolve ? "SERVICE_DISRUPTION_RESOLVED" : "SERVICE_DISRUPTION_PUBLISHED";
        var linePattern = "%" + state.LineId?.ToString("N") + "%";
        var stationPattern = "%" + state.StationId?.ToString("N") + "%";
        var content = JsonSerializer.Serialize(new { disruptionId = state.Id, eventType = type });
        return db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO metro.notifications(operator_id,recipient_user_id,notification_type,channel,content_reference,mandatory)
            SELECT DISTINCT {op},party.user_id,{type},channel.name,CAST({content} AS jsonb),true
            FROM metro.orders o JOIN metro.order_versions v ON v.operator_id=o.operator_id AND v.order_id=o.id AND v.version=o.order_version
            CROSS JOIN LATERAL (VALUES(o.payer_user_id),(o.beneficiary_user_id)) party(user_id)
            CROSS JOIN (VALUES('IN_APP'),('EMAIL_MOCK')) channel(name)
            WHERE o.operator_id={op} AND o.status IN ('PAID','PENDING_ISSUE','ISSUED') AND party.user_id IS NOT NULL
            AND (o.service_date>={today} OR EXISTS(SELECT 1 FROM metro.tickets t JOIN metro.journeys j
                ON j.operator_id=t.operator_id AND j.ticket_id=t.id WHERE t.operator_id={op} AND t.order_id=o.id AND j.status IN ('OPEN','MANUAL_REVIEW')))
            AND ((CAST({state.LineId} AS uuid) IS NOT NULL AND v.fare_snapshot->>'Route' LIKE {linePattern})
                OR (CAST({state.StationId} AS uuid) IS NOT NULL AND v.fare_snapshot->>'Route' LIKE {stationPattern})
                OR EXISTS(SELECT 1 FROM metro.trip_suggestions s WHERE s.operator_id={op} AND s.order_id=o.id
                    AND s.order_version=o.order_version AND s.superseded_at IS NULL AND s.suggested_planned_trip_id={state.PlannedTripId}))
            """, ct);
    }

    private static void RequireLive(PlannedTrip trip)
    {
        if (trip.Status is "COMPLETED" or "CANCELLED") throw Error("TRIP_ALREADY_ENDED", 409);
    }
    private static void ValidatePage(int page) { if (page is < 1 or > 100000) throw Error("INVALID_OPERATIONS_PAGE", 400); }
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static OperationsException Error(string code, int status) => new(code, status);
    private sealed record Replay(string PayloadHash, string Response);
    private sealed record DisruptionState(Guid Id, Guid? LineId, Guid? StationId, Guid? PlannedTripId, string Effect,
        string Status, string Title, string Description, DateTimeOffset OccurredAt, string? PreviousStatus,
        string? AppliedStatus, string? PreviousBoardingStatus);
}
