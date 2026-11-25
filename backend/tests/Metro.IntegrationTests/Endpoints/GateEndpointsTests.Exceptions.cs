using System.Net;
using System.Net.Http.Json;
using Metro.Application.Abstractions.Gates;
using Metro.IntegrationTests.Infrastructure;
using Npgsql;

namespace Metro.IntegrationTests.Endpoints;

public sealed partial class GateEndpointsTests
{
    [PostgreSqlFact, Trait("Category","PostgreSQL")]
    public async Task Wrong_exit_requires_verified_evidence_and_keeps_bought_station_and_original_event()
    {
        using var staff=await AccountAsync("StationStaff"); using var holder=await AccountAsync(); var n=await NetworkAsync(staff); var t=await Issue(n,holder);
        var entered=await Scan(staff,n,Entry(n,t.Qr));
        var wrong=Exit(n,t.Qr) with{StationId=n.C}; var rejected=await Scan(staff,n,wrong); Assert.Equal("Deny",rejected.Decision);
        var r=new GateResolutionRequest(Guid.NewGuid(),wrong.RequestId,"CLOSE_JOURNEY",n.C,"Verified wrong exit","case-wrong-exit",true);
        Assert.Equal(HttpStatusCode.BadRequest,(await Resolve(staff,n,r with{IdentityVerified=false})).StatusCode);
        var resolved=await Resolve(staff,n,r); Assert.Equal(HttpStatusCode.OK,resolved.StatusCode);
        Assert.Equal("Allow",(await resolved.Content.ReadFromJsonAsync<GateResult>())!.Decision);
        Assert.Equal(HttpStatusCode.OK,(await Resolve(staff,n,r)).StatusCode);
        Assert.Equal("USED",await Sql("SELECT status FROM metro.tickets WHERE id=@id",("id",t.Id)));
        var j=entered.Passages[0].JourneyId;
        Assert.Equal(n.B,await Sql("SELECT planned_destination_station_id FROM metro.journeys WHERE id=@id",("id",j)));
        Assert.Equal(n.C,await Sql("SELECT actual_exit_station_id FROM metro.journeys WHERE id=@id",("id",j)));
        Assert.Equal(0L,await Sql("SELECT supplemental_fare_minor FROM metro.journeys WHERE id=@id",("id",j)));
        Assert.Equal("DENY",await Sql("SELECT decision FROM metro.afc_events WHERE id=@id",("id",rejected.Passages[0].EventId)));
        await Assert.ThrowsAsync<PostgresException>(()=>Sql("UPDATE metro.afc_events SET reason_code='REWRITTEN' WHERE id=@id",("id",rejected.Passages[0].EventId)));
    }

    [PostgreSqlFact, Trait("Category","PostgreSQL")]
    public async Task Missing_entry_needs_proven_time_and_context_and_escalation_never_grants_passage()
    {
        using var staff=await AccountAsync("StationStaff"); using var holder=await AccountAsync(); var n=await NetworkAsync(staff); var t=await Issue(n,holder);
        var exit=Exit(n,t.Qr); Assert.Equal("Deny",(await Scan(staff,n,exit)).Decision);
        var support=new GateResolutionRequest(Guid.NewGuid(),exit.RequestId,"ESCALATE",n.B,"Need more evidence","case-open",false);
        var escalated=await Resolve(staff,n,support); Assert.Equal("NEEDS_ADMIN_SUPPORT",(await escalated.Content.ReadFromJsonAsync<GateResult>())!.ReasonCode);
        Assert.Equal(0L,await Sql("SELECT count(*) FROM metro.journeys WHERE ticket_id=@id",("id",t.Id)));
        var r=support with{RequestId=Guid.NewGuid(),Action="SUPPLY_ENTRY_AND_EXIT",IdentityVerified=true,ProvenEnteredAt=DateTimeOffset.UtcNow.AddSeconds(-1).ToOffset(TimeSpan.FromHours(7)),EntryStationId=n.A,EntryPlannedTripId=n.Trip,EntryDirection=0};
        var resolved=await Resolve(staff,n,r); Assert.True(resolved.IsSuccessStatusCode,await resolved.Content.ReadAsStringAsync());
        Assert.Equal("Allow",(await resolved.Content.ReadFromJsonAsync<GateResult>())!.Decision);
        Assert.Equal("USED",await Sql("SELECT status FROM metro.tickets WHERE id=@id",("id",t.Id)));
        Assert.Equal(2L,await Sql("SELECT count(*) FROM metro.afc_events WHERE ticket_id=@id AND event_type LIKE 'MANUAL_%'",("id",t.Id)));
        Assert.Equal(1L,await Sql("SELECT count(*) FROM metro.journeys WHERE ticket_id=@id AND entered_at=@at",("id",t.Id),("at",r.ProvenEnteredAt)));
        Assert.Equal(1L,await Sql("SELECT count(*) FROM metro.afc_events WHERE ticket_id=@id AND event_type='MANUAL_ENTRY' AND event_at=@at AND received_at>=event_at",("id",t.Id),("at",r.ProvenEnteredAt)));
    }

    [PostgreSqlFact, Trait("Category","PostgreSQL")]
    public async Task Mistaken_entry_or_exit_can_be_corrected_once_with_audit_but_later_use_blocks_undo()
    {
        using var staff=await AccountAsync("StationStaff"); using var holder=await AccountAsync(); var n=await NetworkAsync(staff); var t=await Issue(n,holder);
        var entry=Entry(n,t.Qr); Assert.Equal("Allow",(await Scan(staff,n,entry)).Decision);
        var undo=new GateResolutionRequest(Guid.NewGuid(),entry.RequestId,"UNDO_SCAN",n.A,"Scanned by mistake","case-unused",true,PassengerHasNotTravelled:true);
        var reply=await Resolve(staff,n,undo); Assert.True(reply.IsSuccessStatusCode,await reply.Content.ReadAsStringAsync());
        Assert.Equal("Deny",(await reply.Content.ReadFromJsonAsync<GateResult>())!.Decision);
        Assert.Equal("ACTIVE",await Sql("SELECT status FROM metro.tickets WHERE id=@id",("id",t.Id)));
        Assert.Null(await Sql("SELECT first_used_at FROM metro.tickets WHERE id=@id",("id",t.Id)));
        Assert.Equal(HttpStatusCode.Conflict,(await Resolve(staff,n,undo with{RequestId=Guid.NewGuid()})).StatusCode);
        var again=Entry(n,t.Qr); Assert.Equal("Allow",(await Scan(staff,n,again)).Decision);
        var exit=Exit(n,t.Qr); Assert.Equal("Allow",(await Scan(staff,n,exit)).Decision);
        Assert.Equal(HttpStatusCode.Conflict,(await Resolve(staff,n,undo with{RequestId=Guid.NewGuid(),SourceRequestId=again.RequestId})).StatusCode);
        var undoExit=undo with{RequestId=Guid.NewGuid(),SourceRequestId=exit.RequestId,StationId=n.B};
        var reversed=await Resolve(staff,n,undoExit); Assert.True(reversed.IsSuccessStatusCode,await reversed.Content.ReadAsStringAsync());
        Assert.Equal("IN_USE",await Sql("SELECT status FROM metro.tickets WHERE id=@id",("id",t.Id)));
        Assert.Equal(1L,await Sql("SELECT count(*) FROM metro.journeys WHERE ticket_id=@id AND status='OPEN'",("id",t.Id)));
        Assert.Equal("Allow",(await Scan(staff,n,Exit(n,t.Qr))).Decision);
    }

    [PostgreSqlFact, Trait("Category","PostgreSQL")]
    public async Task Audit_failure_rolls_back_journey_ticket_AFC_and_receipt_then_same_request_retries_cleanly()
    {
        using var staff=await AccountAsync("StationStaff"); using var holder=await AccountAsync(); var n=await NetworkAsync(staff); var t=await Issue(n,holder); var r=Entry(n,t.Qr);
        var function="be10_fail_"+Guid.NewGuid().ToString("N");
        await Sql($"CREATE FUNCTION metro.{function}() RETURNS trigger LANGUAGE plpgsql AS $f$ BEGIN RAISE EXCEPTION 'fixture audit unavailable'; END $f$; CREATE TRIGGER {function} BEFORE INSERT ON metro.audit_logs FOR EACH ROW WHEN (NEW.operator_id='{n.Op}'::uuid AND NEW.action='GATE_SCAN') EXECUTE FUNCTION metro.{function}()");
        try
        {
            using var request=new HttpRequestMessage(HttpMethod.Post,Url(n,"scans")){Content=JsonContent.Create(r)};request.Headers.Add("Idempotency-Key",r.RequestId.ToString());
            Assert.Equal(HttpStatusCode.ServiceUnavailable,(await staff.Client.SendAsync(request)).StatusCode);
            Assert.Equal(0L,await Sql("SELECT count(*) FROM metro.journeys WHERE ticket_id=@id",("id",t.Id)));
            Assert.Equal(0L,await Sql("SELECT count(*) FROM metro.afc_events WHERE ticket_id=@id",("id",t.Id)));
            Assert.Equal(0L,await Sql("SELECT count(*) FROM metro.gate_requests WHERE operator_id=@op",("op",n.Op)));
            Assert.Equal("ACTIVE",await Sql("SELECT status FROM metro.tickets WHERE id=@id",("id",t.Id)));
        }
        finally{await Sql($"DROP TRIGGER {function} ON metro.audit_logs; DROP FUNCTION metro.{function}()");}
        Assert.Equal("Allow",(await Scan(staff,n,r)).Decision);
    }

    [PostgreSqlFact, Trait("Category","PostgreSQL")]
    public async Task Lock_timeout_returns_Pending_without_permission_and_result_is_recovered_by_the_same_request()
    {
        using var staff=await AccountAsync("StationStaff"); using var holder=await AccountAsync(); var n=await NetworkAsync(staff); var t=await Issue(n,holder); var r=Entry(n,t.Qr);
        await using(var c=new NpgsqlConnection(PostgreSqlTestConfiguration.LoadRequired().ConnectionString))
        {
            await c.OpenAsync();await using var tx=await c.BeginTransactionAsync();
            await using var cmd=new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@op,14))",c,tx);cmd.Parameters.AddWithValue("op",n.Op.ToString());await cmd.ExecuteNonQueryAsync();
            Assert.Equal("Pending",(await Scan(staff,n,r)).Decision);
            Assert.Equal(0L,await Sql("SELECT count(*) FROM metro.gate_requests WHERE operator_id=@op",("op",n.Op)));
            await tx.RollbackAsync();
        }
        var unknown=await staff.Client.GetFromJsonAsync<GateResult>(Url(n,$"scans/{r.RequestId}"));Assert.Equal("Pending",unknown!.Decision);
        Assert.Equal("Allow",(await Scan(staff,n,r)).Decision);
        var known=await staff.Client.GetFromJsonAsync<GateResult>(Url(n,$"scans/{r.RequestId}"));Assert.Equal("Allow",known!.Decision);
    }
}
