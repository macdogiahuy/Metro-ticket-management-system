using System.Text.Json;
using Metro.Application.Abstractions.Gates;
using Metro.Application.Gates;
using Metro.Application.Ticketing;

namespace Metro.Infrastructure.Gates;

public sealed partial class GateService
{
    private static async Task<string> Snapshot(GateStore s, Guid ticket, Guid journey, CancellationToken ct) =>
        (string)(await s.Scalar("""
            SELECT jsonb_build_object('ticket',jsonb_build_object('status',t.status,'firstUsedAt',t.first_used_at),
              'journey',(SELECT jsonb_build_object('status',j.status,'exitStation',j.actual_exit_station_id,'exitedAt',j.exited_at,
                'exitTrip',j.exit_planned_trip_id,'entryStation',j.actual_entry_station_id,'enteredAt',j.entered_at,
                'entryTrip',j.entry_planned_trip_id,'direction',j.entry_direction,'pair',j.paired_journey_id)
                FROM metro.journeys j WHERE j.operator_id=@op AND j.id=@journey))::text
            FROM metro.tickets t WHERE t.operator_id=@op AND t.id=@ticket
            """, ct, ("ticket", ticket), ("journey", journey)))!;

    private static async Task Change(GateStore s, Guid request, TicketRow t, Guid journey, string action,
        Guid station, Guid? trip, short? direction, Guid? pair, CancellationToken ct, DateTimeOffset? enteredAt = null)
    {
        var before = await Snapshot(s, t.Id, journey, ct);
        if (action is "ENTRY" or "MANUAL_ENTRY")
            await s.Execute("""
                INSERT INTO metro.journeys(id,operator_id,journey_code,ticket_id,planned_origin_station_id,planned_destination_station_id,
                  actual_entry_station_id,status,entered_at,service_date,entry_planned_trip_id,entry_direction,paired_journey_id)
                VALUES(@journey,@op,@journey::text,@ticket,@origin::uuid,@dest::uuid,@station,'OPEN',@entered,@day,@trip::uuid,@direction::smallint,@pair::uuid);
                UPDATE metro.tickets SET status='IN_USE',first_used_at=COALESCE(first_used_at,@entered),updated_at=@now WHERE operator_id=@op AND id=@ticket
                """, ct, ("journey", journey), ("ticket", t.Id), ("origin", t.Origin), ("dest", t.Destination), ("station", station),
                ("entered", enteredAt ?? s.Now), ("day", TicketRules.LocalDate(enteredAt ?? s.Now)), ("trip", trip), ("direction", direction), ("pair", pair));
        else
            await s.Execute("""
                UPDATE metro.journeys SET status='COMPLETED',actual_exit_station_id=@station,exited_at=@now,
                  exit_planned_trip_id=@trip::uuid,supplemental_fare_minor=0,updated_at=@now WHERE operator_id=@op AND id=@journey;
                UPDATE metro.tickets SET status=CASE WHEN @product='SINGLE_TRIP' THEN 'USED' ELSE 'ACTIVE' END,updated_at=@now
                  WHERE operator_id=@op AND id=@ticket
                """, ct, ("journey", journey), ("ticket", t.Id), ("station", station), ("trip", trip), ("product", t.Product));
        var after = await Snapshot(s, t.Id, journey, ct);
        await s.Execute("""
            INSERT INTO metro.gate_changes(operator_id,request_id,ticket_id,journey_id,action,before_state,after_state,created_at)
            VALUES(@op,@request,@ticket,@journey,@action,@before::jsonb,@after::jsonb,@now)
            """, ct, ("request", request), ("ticket", t.Id), ("journey", journey), ("action", action), ("before", before), ("after", after));
    }
    private static Task Event(GateStore s, Guid request, Guid id, TicketRow t, Guid? journey, string action,
        Guid station, string device, string decision, string reason, Guid? trip, short? direction, string? manualReason, CancellationToken ct,
        DateTimeOffset? eventAt = null) =>
        s.Execute("""
            INSERT INTO metro.afc_events(id,operator_id,event_code,idempotency_key,journey_id,ticket_id,credential_id,event_type,
              station_id,device_code,event_at,received_at,decision,reason_code,actor_user_id,manual_reason,planned_trip_id,direction,correlation_id)
            VALUES(@id,@op,@id::text,@request::text||':'||@ticket::text||':'||@action,@journey::uuid,@ticket,@credential,@action,
              @station,@device,@eventAt,@now,@decision,@reason,@actor,@manual::text,@trip::uuid,@direction::smallint,@id)
            """, ct, ("id", id), ("request", request), ("ticket", t.Id), ("credential", t.Credential), ("journey", journey), ("action", action),
            ("station", station), ("device", device), ("decision", decision), ("reason", reason), ("manual", manualReason), ("trip", trip), ("direction", direction), ("eventAt", eventAt ?? s.Now));

    public Task<GateResult> ResolveAsync(string actor, string? code, GateResolutionRequest r, string key, CancellationToken ct)
    {
        GateRules.Validate(r, key);
        return Transaction(actor, code, r.RequestId, r, s => Resolve(s, r, ct), ct);
    }
    private static async Task<GateResult> Resolve(GateStore s, GateResolutionRequest r, CancellationToken ct)
    {
        var source = await s.ReadReceipt(r.SourceRequestId, ct) ?? throw new GateException("GATE_SOURCE_NOT_FOUND", 404);
        var original = JsonSerializer.Deserialize<GateResult>(source.Response, GateStore.Json)!;
        if (source.Kind != "SCAN") throw new GateException("GATE_SOURCE_REQUIRES_ADMIN_SUPPORT", 409);
        if (await s.Scalar("SELECT EXISTS(SELECT 1 FROM metro.stations WHERE operator_id=@op AND id=@id)", ct, ("id", r.StationId)) is not true)
            throw new GateException("GATE_STATION_NOT_FOUND", 404);
        // An unreadable/unknown QR still needs a durable support record, without inventing a ticket.
        if (r.Action == "ESCALATE")
        {
            var support = new GateResult(r.RequestId, "Deny", "NEEDS_ADMIN_SUPPORT", s.Now, []);
            await SaveResolution(s, r, support, ct); return support;
        }
        if(r.Action=="CLOSE_SUPPORT")
        {
            await RequireAdmin(s,ct);
            if(await s.Scalar("SELECT EXISTS(SELECT 1 FROM metro.gate_resolutions WHERE operator_id=@op AND source_request_id=@source AND action='ESCALATE')",ct,("source",r.SourceRequestId)) is not true)
                throw new GateException("GATE_SUPPORT_CASE_NOT_FOUND",404);
            var reviewed=new GateResult(r.RequestId,"Deny","SUPPORT_CLOSED_NO_PASSAGE",s.Now,[]);
            await SaveResolution(s,r,reviewed,ct);return reviewed;
        }
        if (original.Passages.Count == 0) throw new GateException("GATE_SOURCE_REQUIRES_ADMIN_SUPPORT", 409);
        var credentials = (await s.Rows<CredentialRef>("""
            SELECT credential_id AS "Id" FROM metro.afc_events WHERE operator_id=@op AND id=ANY(@ids)
            """, ct, ("ids", original.Passages.Select(p => p.EventId).ToArray()))).Select(c => c.Id).ToArray();
        var tickets = await s.Tickets(credentials, ct);
        await s.LockHolders(tickets.Where(t => t.Holder is not null).Select(t => t.Holder!), ct);
        await s.Execute("SELECT id FROM metro.tickets WHERE operator_id=@op AND id=ANY(@ids) ORDER BY id FOR UPDATE", ct, ("ids", tickets.Select(t => t.Id).ToArray()));
        tickets = await s.Tickets(credentials, ct);
        var journeys = await s.Journeys(tickets.Select(t => t.Id).ToArray(), ct);
        if (tickets.Count != original.Passages.Count || tickets.Count == 2 && !r.PairPresentConfirmed)
            throw new GateException("GUARDIAN_PAIR_REQUIRED", 409);
        if (r.Action == "UNDO_SCAN") return await Undo(s, r, original, tickets, ct);
        if (r.Action == "CLOSE_JOURNEY")
        {
            if (journeys.Count != tickets.Count || journeys.Any(j => j.EnteredAt is null)) throw new GateException("GATE_ENTRY_EVIDENCE_REQUIRED", 409);
            if (await Pair(s, tickets, journeys, "EXIT", r.PairPresentConfirmed, ct) is { } pairError) throw new GateException(pairError, 409);
        }
        else
        {
            if (journeys.Count != 0 || r.ProvenEnteredAt > s.Now || tickets.Any(t => t.Status != "ACTIVE" ||
                t.Start is null || t.End is null || t.Start > r.ProvenEnteredAt || t.End <= r.ProvenEnteredAt))
                throw new GateException("GATE_ENTRY_EVIDENCE_INVALID", 409);
            if (await s.Scalar("""
                SELECT EXISTS(SELECT 1 FROM metro.journeys j JOIN metro.tickets t ON t.operator_id=j.operator_id AND t.id=j.ticket_id
                 WHERE j.operator_id=@op AND t.holder_user_id=ANY(@holders) AND j.status IN ('OPEN','MANUAL_REVIEW'))
                """, ct, ("holders", tickets.Select(t => t.Holder!).ToArray())) is true) throw new GateException("JOURNEY_ALREADY_OPEN", 409);
            var proven = r.ProvenEnteredAt!.Value;
            if (await s.Scalar("""
                SELECT EXISTS(SELECT 1 FROM metro.planned_trips p JOIN metro.planned_stop_times st
                 ON st.operator_id=p.operator_id AND st.planned_trip_id=p.id AND st.station_id=@station
                 WHERE p.operator_id=@op AND p.id=@trip AND p.direction=@direction AND p.service_date=@day)
                """, ct, ("station", r.EntryStationId), ("trip", r.EntryPlannedTripId), ("direction", r.EntryDirection), ("day", TicketRules.LocalDate(proven))) is not true ||
                tickets.Any(t => t.Product == "SINGLE_TRIP" && (t.Origin != r.EntryStationId || t.Day != TicketRules.LocalDate(proven))))
                throw new GateException("GATE_ENTRY_EVIDENCE_INVALID", 409);
            if (await Pair(s, tickets, journeys, "ENTRY", r.PairPresentConfirmed, ct) is { } pairError) throw new GateException(pairError, 409);
        }
        var ids = tickets.ToDictionary(t => t.Id, t => journeys.SingleOrDefault(j => j.Ticket == t.Id)?.Id ?? Guid.NewGuid());
        // Supplement and exit are distinct immutable events, committed together.
        if (r.Action == "SUPPLY_ENTRY_AND_EXIT")
        {
            foreach (var t in tickets)
            {
                await Change(s, r.RequestId, t, ids[t.Id], "MANUAL_ENTRY", r.EntryStationId!.Value,
                    r.EntryPlannedTripId, r.EntryDirection, tickets.Count == 2 ? ids[tickets.Single(v => v.Id != t.Id).Id] : null, ct, r.ProvenEnteredAt);
                await Event(s, r.RequestId, Guid.NewGuid(), t, ids[t.Id], "MANUAL_ENTRY", r.EntryStationId.Value, "STAFF_SUPPORT", "ALLOW",
                    "EVIDENCED_ENTRY", r.EntryPlannedTripId, r.EntryDirection, r.Reason, ct, r.ProvenEnteredAt);
            }
        }
        var passages = new List<GatePassage>();
        foreach (var t in tickets)
        {
            var id = Guid.NewGuid();
            await Change(s, r.RequestId, t, ids[t.Id], "MANUAL_EXIT", r.StationId, null, null, null, ct);
            await Event(s, r.RequestId, id, t, ids[t.Id], "MANUAL_EXIT", r.StationId, "STAFF_SUPPORT", "ALLOW", "VERIFIED_EXIT", null, null, r.Reason, ct);
            passages.Add(new(t.Id, ids[t.Id], id));
        }
        var result = new GateResult(r.RequestId, "Allow", "VERIFIED_EXIT", s.Now, passages);
        await SaveResolution(s, r, result, ct); return result;
    }
    private static async Task SaveResolution(GateStore s, GateResolutionRequest r, GateResult result, CancellationToken ct)
    {
        await s.Save(r.RequestId, r, result, "RESOLUTION", ct);
        await s.Execute("""
            INSERT INTO metro.gate_resolutions(operator_id,request_id,source_request_id,action,reason,evidence_reference,identity_verified,passenger_has_not_travelled)
            VALUES(@op,@id,@source,@action,@reason,@evidence,@verified,@unused)
            """, ct, ("id", r.RequestId), ("source", r.SourceRequestId), ("action", r.Action), ("reason", r.Reason),
            ("evidence", r.EvidenceReference), ("verified", r.IdentityVerified), ("unused", r.PassengerHasNotTravelled));
        if(r.Action=="ESCALATE")await NotifySupport(s,r,ct);
    }
    private static async Task<GateResult> Undo(GateStore s, GateResolutionRequest r, GateResult original, List<TicketRow> tickets, CancellationToken ct)
    {
        if (original.Decision != "Allow") throw new GateException("GATE_SCAN_NOT_REVERSIBLE", 409);
        var changes = await s.Rows<ChangeRow>("""
            SELECT change_no AS "Number",ticket_id AS "Ticket",journey_id AS "Journey",action AS "Action",
              before_state::text AS "Before",after_state::text AS "After"
            FROM metro.gate_changes WHERE operator_id=@op AND request_id=@id
            """, ct, ("id", original.RequestId));
        if (changes.Count != tickets.Count) throw new GateException("GATE_SCAN_NOT_REVERSIBLE", 409);
        foreach (var change in changes)
        {
            if (change.Action is not ("ENTRY" or "EXIT") || await s.Scalar("""
                SELECT EXISTS(SELECT 1 FROM metro.gate_changes WHERE operator_id=@op AND ticket_id=@ticket AND change_no>@number)
                  OR EXISTS(SELECT 1 FROM metro.gate_undo_authorizations WHERE operator_id=@op AND source_change_no=@number)
                """, ct, ("ticket", change.Ticket), ("number", change.Number)) is true ||
                await s.Scalar("SELECT @current::jsonb=@expected::jsonb", ct, ("current", await Snapshot(s, change.Ticket, change.Journey, ct)), ("expected", change.After)) is not true)
                throw new GateException("GATE_SCAN_HAS_LATER_CHANGES", 409);
        }
        var passages = new List<GatePassage>();
        foreach (var change in changes)
        {
            using var state = JsonDocument.Parse(change.Before);
            var target = state.RootElement.GetProperty("ticket").GetProperty("status").GetString()!;
            await s.Execute("""
                INSERT INTO metro.gate_undo_authorizations(operator_id,source_change_no,request_id,ticket_id,target_status,transaction_id)
                VALUES(@op,@source,@request,@ticket,@status,pg_current_xact_id()::text);
                UPDATE metro.journeys SET status=CASE WHEN @action='ENTRY' THEN 'CANCELLED' ELSE @before::jsonb->'journey'->>'status' END,
                  actual_exit_station_id=CASE WHEN @action='ENTRY' THEN NULL ELSE (@before::jsonb->'journey'->>'exitStation')::uuid END,
                  exited_at=CASE WHEN @action='ENTRY' THEN NULL ELSE (@before::jsonb->'journey'->>'exitedAt')::timestamptz END,
                  exit_planned_trip_id=CASE WHEN @action='ENTRY' THEN NULL ELSE (@before::jsonb->'journey'->>'exitTrip')::uuid END,updated_at=@now
                WHERE operator_id=@op AND id=@journey;
                UPDATE metro.tickets SET status=@status,first_used_at=(@before::jsonb->'ticket'->>'firstUsedAt')::timestamptz,updated_at=@now WHERE operator_id=@op AND id=@ticket
                """, ct, ("source", change.Number), ("request", r.RequestId), ("ticket", change.Ticket), ("status", target), ("action", change.Action), ("before", change.Before), ("journey", change.Journey));
            var eventId = Guid.NewGuid();
            await Event(s, r.RequestId, eventId, tickets.Single(t => t.Id == change.Ticket), change.Journey,
                change.Action == "ENTRY" ? "MANUAL_EXIT" : "MANUAL_ENTRY", r.StationId, "STAFF_SUPPORT", "REVIEW", "SCAN_CORRECTED", null, null, r.Reason, ct);
            passages.Add(new(change.Ticket, change.Journey, eventId));
        }
        var result = new GateResult(r.RequestId, "Deny", "SCAN_CORRECTED_NO_PASSAGE", s.Now, passages);
        await SaveResolution(s, r, result, ct); return result;
    }
    public async Task<GateTicketLookup> LookupAsync(string actor, string? code, GateLookupRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.TicketCode) || request.TicketCode.Length > 60 || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 1000)
            throw new GateException("INVALID_GATE_LOOKUP", 400);
        GateTicketLookup? found = null;
        var outcome = await Transaction(actor, code, Guid.NewGuid(), null, async s =>
        {
            found = (await s.Rows<GateTicketLookup>("""
                SELECT t.id AS "TicketId",t.ticket_code AS "TicketCode",t.status AS "Status",p.product_type AS "ProductType",
                  t.effective_from AS "EffectiveFrom",t.expires_at AS "ExpiresAt",t.origin_station_id AS "OriginStationId",
                  t.destination_station_id AS "DestinationStationId",j.id AS "OpenJourneyId",j.paired_journey_id AS "PairedJourneyId",u.full_name AS "PassengerName"
                FROM metro.tickets t JOIN metro.ticket_products p ON p.operator_id=t.operator_id AND p.id=t.ticket_product_id
                  LEFT JOIN metro.user_profiles u ON u.user_id=t.holder_user_id
                  LEFT JOIN metro.journeys j ON j.operator_id=t.operator_id AND j.ticket_id=t.id AND j.status IN ('OPEN','MANUAL_REVIEW')
                WHERE t.operator_id=@op AND t.ticket_code=@code
                """, ct, ("code", request.TicketCode))).SingleOrDefault() ?? throw new GateException("TICKET_NOT_FOUND", 404);
            await s.Audit(found.TicketId, "LOOKUP", request.Reason, ct);
            return new(Guid.Empty, "Deny", "LOOKUP_ONLY", s.Now, []);
        }, ct);
        return outcome.Decision == "Pending" || found is null ? throw new GateException("GATE_LOOKUP_UNAVAILABLE", 503) : found;
    }
    private sealed record CredentialRef(Guid Id);
    private sealed record ChangeRow(long Number, Guid Ticket, Guid Journey, string Action, string Before, string After);
}
