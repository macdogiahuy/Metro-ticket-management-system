using System.Text.Json;
using Metro.Application.Abstractions.Gates;
using Metro.Application.Gates;
using Metro.Application.Ticketing;
using Metro.Infrastructure.Persistence.Context;
using Metro.Infrastructure.Ticketing;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Metro.Infrastructure.Gates;

public sealed partial class GateService(ApplicationDbContext db) : IGateService
{
    private async Task<GateResult> Transaction(string actor, string? code, Guid id, object? payload,
        Func<GateStore, Task<GateResult>> action, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length > 60) throw new GateException("GATE_OPERATOR_REQUIRED", 400);
        try
        {
            await using var c = new NpgsqlConnection(db.Database.GetConnectionString()); await c.OpenAsync(ct);
            await using var tx = await c.BeginTransactionAsync(ct);
            await using var find = new NpgsqlCommand("SELECT id FROM metro.operators WHERE code=@code AND status='ACTIVE'", c, tx);
            find.Parameters.AddWithValue("code", code);
            var op = await find.ExecuteScalarAsync(ct) as Guid? ?? throw new GateException("OPERATOR_NOT_FOUND", 404);
            var s = new GateStore(c, tx, op, actor);
            await s.Execute("""
                SET LOCAL lock_timeout='5s';
                SELECT pg_advisory_xact_lock_shared(73190624011222);
                SELECT pg_advisory_xact_lock_shared(hashtextextended(@actor,4));
                SELECT pg_advisory_xact_lock_shared(hashtextextended(@op::text,14));
                SELECT pg_advisory_xact_lock(hashtextextended(@op::text||':gate:'||@id::text,0));
                """, ct, ("id", id));
            if (!await s.Staff(ct)) throw new GateException("GATE_STAFF_REQUIRED", 403);
            s.Now = new DateTimeOffset((DateTime)(await s.Scalar("SELECT clock_timestamp()", ct))!);
            var saved = await s.ReadReceipt(id, ct);
            if (saved is not null)
            {
                if (saved.Actor != actor) throw new GateException("GATE_REQUEST_FORBIDDEN", 403);
                if (payload is not null && GateStore.Hash(payload) != saved.Hash) throw new GateException("IDEMPOTENCY_KEY_CONFLICT", 409);
                return JsonSerializer.Deserialize<GateResult>(saved.Response, GateStore.Json)!;
            }
            var result = await action(s);
            await tx.CommitAsync(ct); // Allow can escape only after the commit is acknowledged.
            return result;
        }
        catch (PostgresException e) when (e.SqlState is "40001" or "40P01" or "55P03" or "57014") { return Pending(id); }
        catch (PostgresException e) { throw new GateException(e.SqlState == "23505" ? "GATE_STATE_CONFLICT" : "GATE_STORAGE_UNAVAILABLE", e.SqlState == "23505" ? 409 : 503); }
        catch (NpgsqlException) { return Pending(id); }
        catch (TimeoutException) { return Pending(id); }
    }
    private static GateResult Pending(Guid id) => new(id, "Pending", "GATE_RESULT_UNCONFIRMED", null, []);
    public Task<GateResult> ResultAsync(string actor, string? code, Guid id, CancellationToken ct)
    {
        if (id == Guid.Empty) throw new GateException("INVALID_GATE_REQUEST_ID", 400);
        return Transaction(actor, code, id, null, _ => Task.FromResult(Pending(id)), ct);
    }
    public Task<GateResult> ScanAsync(string actor, string? code, GateScanRequest r, string key, CancellationToken ct)
    {
        GateRules.Validate(r, key);
        return Transaction(actor, code, r.RequestId, r, s => Scan(s, r, ct), ct);
    }
    private static async Task<GateResult> Scan(GateStore s, GateScanRequest r, CancellationToken ct)
    {
        if (await s.Scalar("SELECT EXISTS(SELECT 1 FROM metro.stations WHERE operator_id=@op AND id=@id)", ct, ("id", r.StationId)) is not true)
            throw new GateException("GATE_STATION_NOT_FOUND", 404);
        if (r.PlannedTripId is not null && await s.Scalar("SELECT EXISTS(SELECT 1 FROM metro.planned_trips WHERE operator_id=@op AND id=@id)", ct, ("id", r.PlannedTripId)) is not true)
            throw new GateException("GATE_TRIP_NOT_FOUND", 404);
        var tokens = r.QrTokens.Select(TicketQrCodec.Read).ToArray();
        var tickets = new List<TicketRow>();
        string? denial = null;
        if (tokens.Any(t => t is null || t.Operator != s.Operator)) denial = "QR_INVALID";
        else
        {
            tickets = await s.Tickets(tokens.Select(t => t!.Credential).ToArray(), ct);
            if (tickets.Count != tokens.Length || tickets.Select(t => t.Id).Distinct().Count() != tickets.Count ||
                tokens.Any(t => !tickets.Any(v => v.Credential == t!.Credential && v.Reference == t.TicketReference && v.Version == t.Version && v.KeyId == t.KeyId &&
                    TicketQrCodec.Matches(r.QrTokens[Array.IndexOf(tokens, t)], v.TokenHash))))
            { denial = "QR_INVALID"; tickets.Clear(); }
        }
        if (denial is null)
        {
            await s.LockHolders(tickets.Where(t => t.Holder is not null).Select(t => t.Holder!), ct);
            await s.Execute("SELECT id FROM metro.tickets WHERE operator_id=@op AND id=ANY(@ids) ORDER BY id FOR UPDATE", ct, ("ids", tickets.Select(t => t.Id).ToArray()));
            await s.Execute("SELECT id FROM metro.ticket_credentials WHERE operator_id=@op AND id=ANY(@ids) ORDER BY id FOR SHARE", ct, ("ids", tickets.Select(t => t.Credential).ToArray()));
            tickets = await s.Tickets(tickets.Select(t => t.Credential).ToArray(), ct);
            s.Now = new DateTimeOffset((DateTime)(await s.Scalar("SELECT clock_timestamp()", ct))!);
            // QR support and scans share the same exclusive holder lock.
            if (tickets.Any(t => t.CredentialStatus != "ACTIVE" || !r.QrTokens.Any(q => TicketQrCodec.Matches(q, t.TokenHash)))) denial = "QR_REVOKED";
        }
        var journeys = await s.Journeys(tickets.Select(t => t.Id).ToArray(), ct);
        if (denial is null) denial = await Pair(s, tickets, journeys, r.Action, r.PairPresentConfirmed, ct);
        if (denial is null && r.Action == "ENTRY")
        {
            denial = await Entry(s, r, tickets, ct);
            if (denial is null && await s.Scalar("""
                SELECT EXISTS(SELECT 1 FROM metro.journeys j JOIN metro.tickets t ON t.operator_id=j.operator_id AND t.id=j.ticket_id
                WHERE j.operator_id=@op AND t.holder_user_id=ANY(@holders) AND j.status IN ('OPEN','MANUAL_REVIEW'))
                """, ct, ("holders", tickets.Select(t => t.Holder!).ToArray())) is true) denial = "JOURNEY_ALREADY_OPEN";
        }
        if (denial is null && r.Action == "EXIT")
        {
            if (tickets.Any(t => t.Status != "IN_USE") || journeys.Count != tickets.Count || journeys.Any(j => j.Status != "OPEN" || j.EnteredAt is null)) denial = "MISSING_ENTRY_OR_NEEDS_SUPPORT";
            else if (tickets.Any(t => journeys.Single(j => j.Ticket == t.Id).EntryStation == r.StationId ||
                t.Product == "SINGLE_TRIP" && t.Destination != r.StationId)) denial = "EXIT_REQUIRES_VERIFICATION";
        }
        if (await s.Scalar("SELECT EXISTS(SELECT 1 FROM metro.stations WHERE operator_id=@op AND id=@station)", ct, ("station", r.StationId)) is not true)
            throw new GateException("GATE_STATION_NOT_FOUND", 404);
        var passages = new List<GatePassage>();
        var ids = tickets.ToDictionary(t => t.Id, _ => Guid.NewGuid());
        foreach (var t in tickets)
        {
            var journey = journeys.SingleOrDefault(j => j.Ticket == t.Id);
            Guid? jid = journey?.Id;
            var eventId = Guid.NewGuid();
            if (denial is null)
            {
                jid = r.Action == "ENTRY" ? ids[t.Id] : journey!.Id;
                await Change(s, r.RequestId, t, jid.Value, r.Action, r.StationId, r.PlannedTripId, r.Direction,
                    tickets.Count == 2 ? ids[tickets.Single(v => v.Id != t.Id).Id] : null, ct);
            }
            await Event(s, r.RequestId, eventId, t, jid, r.Action == "ENTRY" ? "TAP_IN" : "TAP_OUT",
                r.StationId, r.DeviceCode, denial is null ? "ALLOW" : "DENY", denial ?? "PASSAGE_RECORDED",
                r.PlannedTripId, r.Direction, null, ct);
            passages.Add(new(t.Id, jid, eventId));
        }
        var result = new GateResult(r.RequestId, denial is null ? "Allow" : "Deny", denial ?? "PASSAGE_RECORDED", s.Now, passages);
        await s.Save(r.RequestId, r, result, "SCAN", ct);
        return result;
    }
    private static async Task<string?> Pair(GateStore s, List<TicketRow> tickets, List<JourneyRow> journeys, string action, bool confirmed, CancellationToken ct)
    {
        if (action == "EXIT" && journeys.Any(j => j.Pair is not null) && tickets.Count != 2) return "GUARDIAN_PAIR_REQUIRED";
        if (tickets.Count(t => t.GuardianLink is not null) > 1) return "GUARDIAN_PAIR_MISMATCH";
        var child = tickets.SingleOrDefault(t => t.GuardianLink is not null);
        if (child is null) return tickets.Count == 1 ? null : "GUARDIAN_PAIR_REQUIRED";
        if (tickets.Count != 2 || !confirmed) return "GUARDIAN_PAIR_REQUIRED";
        var guardian = tickets.Single(t => t.Id != child.Id);
        if (child.Related != guardian.Id || child.Guardian != guardian.Holder || child.Holder == guardian.Holder) return "GUARDIAN_PAIR_MISMATCH";
        if (action == "EXIT")
            return journeys.Count == 2 && journeys[0].Pair == journeys[1].Id && journeys[1].Pair == journeys[0].Id ? null : "GUARDIAN_PAIR_MISMATCH";
        var day = TicketRules.LocalDate(s.Now);
        var eligible = await s.Scalar("""
            SELECT EXISTS(SELECT 1 FROM metro.guardian_links g JOIN metro.user_profiles p ON p.user_id=g.child_user_id
              WHERE g.operator_id=@op AND g.id=@link AND g.guardian_user_id=@guardian AND g.child_user_id=@child
              AND g.status='ACTIVE' AND g.verification_type='GUARDIANSHIP' AND g.verified_at IS NOT NULL
              AND (g.valid_from IS NULL OR g.valid_from<=@day) AND (g.valid_to IS NULL OR g.valid_to>=@day)
              AND p.birth_date<=@day AND p.birth_date+interval '6 years'>@day)
            """, ct, ("link", child.GuardianLink), ("guardian", guardian.Holder), ("child", child.Holder), ("day", day));
        return eligible is true ? null : "GUARDIAN_LINK_INVALID";
    }
    private static async Task<string?> Entry(GateStore s, GateScanRequest r, List<TicketRow> tickets, CancellationToken ct)
    {
        foreach (var t in tickets)
        {
            if (t.Holder is null || t.Status != "ACTIVE" || t.Start is null || t.End is null || t.Start > s.Now || t.End <= s.Now) return "TICKET_NOT_VALID_FOR_ENTRY";
            if (await s.Scalar("""
                SELECT EXISTS(SELECT 1 FROM public."AspNetUsers" u JOIN metro.user_profiles p ON p.user_id=u."Id"
                 WHERE u."Id"=@holder AND u."EmailConfirmed" AND p.status='ACTIVE'
                   AND NOT EXISTS(SELECT 1 FROM metro.operator_accounts m WHERE m.operator_id=@op AND m.user_id=u."Id" AND m.status<>'ACTIVE'))
                """, ct, ("holder", t.Holder)) is not true) return "PASSENGER_NOT_ACTIVE";
            if (t.Product == "SINGLE_TRIP" && (t.Origin != r.StationId || t.Day != TicketRules.LocalDate(s.Now))) return "WRONG_ENTRY_STATION_OR_DAY";
        }
        // Lock the operational rows as well as coordinating with BE-14's operator advisory lock.
        await s.Execute("SELECT id FROM metro.planned_trips WHERE operator_id=@op AND id=@trip FOR SHARE", ct, ("trip", r.PlannedTripId));
        var trip = (await s.Rows<TripContext>("""
            SELECT p.direction AS "Direction",p.service_date AS "Day",p.status AS "Status",p.boarding_status AS "Boarding",
              p.boarding_station_id AS "Station",l.status AS "LineStatus",st.status AS "StationStatus",
              a.status AS "ActualStatus",a.id AS "ActualId",z.actual_arrival_at AS "Arrival",z.actual_departure_at AS "Departure",
              z.sequence_no AS "Sequence",load.observed_load_kg AS "Load",load.max_load_kg AS "Maximum",
              (sv.status='PUBLISHED' AND cal.status='PUBLISHED' AND sv.effective_from<=@now AND (sv.effective_to IS NULL OR sv.effective_to>@now)
                AND cal.effective_from<=p.service_date AND (cal.effective_to IS NULL OR cal.effective_to>=p.service_date)) AS "Published"
            FROM metro.planned_trips p JOIN metro.lines l ON l.operator_id=p.operator_id AND l.id=p.line_id
              JOIN metro.schedule_versions sv ON sv.operator_id=p.operator_id AND sv.id=p.schedule_version_id
              JOIN metro.service_calendars cal ON cal.operator_id=sv.operator_id AND cal.id=sv.service_calendar_id
              JOIN metro.stations st ON st.operator_id=p.operator_id AND st.id=@station
              LEFT JOIN metro.actual_trips a ON a.operator_id=p.operator_id AND a.planned_trip_id=p.id
              LEFT JOIN metro.actual_stop_times z ON z.operator_id=a.operator_id AND z.actual_trip_id=a.id AND z.station_id=@station
              LEFT JOIN metro.v_trip_load_status load ON load.operator_id=p.operator_id AND load.planned_trip_id=p.id
            WHERE p.operator_id=@op AND p.id=@trip
            """, ct, ("trip", r.PlannedTripId), ("station", r.StationId))).SingleOrDefault();
        if (trip is null || !trip.Published || trip.Day != TicketRules.LocalDate(s.Now) || trip.Direction != r.Direction ||
            trip.Status is not ("READY" or "IN_SERVICE" or "DELAYED") || trip.ActualStatus is not ("READY" or "IN_SERVICE" or "DELAYED") ||
            trip.Boarding != "OPEN" || trip.Station != r.StationId || trip.LineStatus != "ACTIVE" || trip.StationStatus != "ACTIVE" ||
            trip.Arrival is null || trip.Arrival > s.Now || trip.Departure is not null) return "TRIP_NOT_BOARDING_AT_STATION";
        if (await s.Scalar("""
            SELECT EXISTS(SELECT 1 FROM metro.actual_stop_times WHERE operator_id=@op AND actual_trip_id=@id
              AND sequence_no>@seq AND actual_arrival_at<=@now)
            """, ct, ("id", trip.ActualId), ("seq", trip.Sequence)) is true) return "TRIP_ALREADY_LEFT_STATION";
        // Match the first leg from the immutable fare snapshot; a transfer journey need not end on this line.
        foreach (var t in tickets.Where(t => t.Product == "SINGLE_TRIP"))
            if (await s.Scalar("""
                SELECT EXISTS(SELECT 1 FROM metro.tickets t JOIN metro.order_versions v ON v.operator_id=t.operator_id AND v.order_id=t.order_id AND v.version=t.order_version
                  JOIN metro.planned_trips p ON p.operator_id=t.operator_id AND p.id=@trip
                  JOIN metro.planned_stop_times origin ON origin.operator_id=p.operator_id AND origin.planned_trip_id=p.id AND origin.station_id=@station
                  JOIN metro.planned_stop_times dest ON dest.operator_id=p.operator_id AND dest.planned_trip_id=p.id
                    AND dest.station_id=COALESCE((v.fare_snapshot->>'FirstLegEnd')::uuid,t.destination_station_id) AND dest.sequence_no>origin.sequence_no
                  WHERE t.operator_id=@op AND t.id=@ticket
                    AND ((v.fare_snapshot->>'FirstLineId') IS NULL OR p.line_id=(v.fare_snapshot->>'FirstLineId')::uuid)
                    AND ((v.fare_snapshot->>'FirstDirection') IS NULL OR p.direction=(v.fare_snapshot->>'FirstDirection')::smallint))
                """, ct, ("trip", r.PlannedTripId), ("station", r.StationId), ("ticket", t.Id)) is not true) return "WRONG_ENTRY_DIRECTION_OR_ROUTE";
        return GateRules.EntryLoad(trip.Load, trip.Maximum);
    }
    private sealed record TripContext(short Direction, DateOnly Day, string Status, string Boarding, Guid? Station,
        string LineStatus, string StationStatus, string? ActualStatus, Guid? ActualId, DateTimeOffset? Arrival,
        DateTimeOffset? Departure, short? Sequence, decimal? Load, decimal? Maximum, bool Published);
}
