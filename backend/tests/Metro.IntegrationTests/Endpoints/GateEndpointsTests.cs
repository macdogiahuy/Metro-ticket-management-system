using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Metro.Application.Abstractions.Gates;
using Metro.IntegrationTests.Infrastructure;
using Npgsql;

namespace Metro.IntegrationTests.Endpoints;

public sealed partial class GateEndpointsTests
{
    [PostgreSqlFact, Trait("Category","PostgreSQL")]
    public async Task All_routes_require_staff_and_active_operator_membership_and_support_Admin()
    {
        using var staff=await AccountAsync("StationStaff"); using var passenger=await AccountAsync(); using var outsider=await AccountAsync("StationStaff");
        var n=await NetworkAsync(staff); using var anonymous=factory.CreateClient();
        foreach(var client in new[]{anonymous,passenger.Client,outsider.Client})
        {
            var expected=client==anonymous ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;
            Assert.Equal(expected,(await client.GetAsync(Url(n,$"scans/{Guid.NewGuid()}"))).StatusCode);
            Assert.Equal(expected,(await client.PostAsJsonAsync(Url(n,"lookup"),new GateLookupRequest("ANY","support"))).StatusCode);
            foreach(var route in new[]{"scans","resolutions"})
            {
                var id=Guid.NewGuid();
                using var request=new HttpRequestMessage(HttpMethod.Post,Url(n,route)){Content=JsonContent.Create(route=="scans" ? (object)(Entry(n,"invalid") with{RequestId=id}) :
                    new GateResolutionRequest(id,Guid.NewGuid(),"ESCALATE",n.B,"Support","case-1",false))};
                // A valid key is needed to reach the service for a staff user without operator membership.
                request.Headers.Add("Idempotency-Key",id.ToString());
                Assert.Equal(expected,(await client.SendAsync(request)).StatusCode);
            }
        }
        using var admin=await AccountAsync("Admin");
        await Sql("INSERT INTO metro.operator_accounts(operator_id,user_id,account_type,mfa_required) VALUES(@op,@id,'ADMIN',true)",("op",n.Op),("id",admin.Id));
        Assert.Equal("Deny",(await Scan(admin,n,Entry(n,"bad-qr"))).Decision);
        Assert.Equal(HttpStatusCode.NotFound,(await staff.Client.GetAsync($"/api/v1/gate/scans/{Guid.NewGuid()}?operatorCode=unknown")).StatusCode);
    }

    [PostgreSqlFact, Trait("Category","PostgreSQL")]
    public async Task Entry_exit_replay_and_concurrent_scans_record_one_journey_and_never_change_load()
    {
        using var staff=await AccountAsync("StationStaff"); using var holder=await AccountAsync(); var n=await NetworkAsync(staff); var t=await Issue(n,holder);
        var entry=Entry(n,t.Qr); var repeated=await Task.WhenAll(Scan(staff,n,entry),Scan(staff,n,entry));
        Assert.All(repeated,r=>Assert.Equal("Allow",r.Decision)); Assert.Equal(repeated[0].Passages[0],repeated[1].Passages[0]);
        Assert.Equal(1L,await Sql("SELECT count(*) FROM metro.journeys WHERE ticket_id=@id",("id",t.Id)));
        var journey=repeated[0].Passages[0].JourneyId;
        Assert.Equal(n.Trip,await Sql("SELECT entry_planned_trip_id FROM metro.journeys WHERE id=@id",("id",journey)));
        Assert.Equal((short)0,await Sql("SELECT entry_direction FROM metro.journeys WHERE id=@id",("id",journey)));
        Assert.Equal("Deny",(await Scan(staff,n,Entry(n,t.Qr))).Decision);
        var recovered=await staff.Client.GetFromJsonAsync<GateResult>(Url(n,$"scans/{entry.RequestId}")); Assert.Equal("Allow",recovered!.Decision);
        using var conflict=new HttpRequestMessage(HttpMethod.Post,Url(n,"scans")){Content=JsonContent.Create(entry with{StationId=n.C})};
        conflict.Headers.Add("Idempotency-Key",entry.RequestId.ToString()); Assert.Equal(HttpStatusCode.Conflict,(await staff.Client.SendAsync(conflict)).StatusCode);
        var exit=Exit(n,t.Qr); var exits=await Task.WhenAll(Scan(staff,n,exit),Scan(staff,n,Exit(n,t.Qr)));
        Assert.Single(exits,r=>r.Decision=="Allow"); Assert.Single(exits,r=>r.Decision=="Deny");
        Assert.Equal("USED",await Sql("SELECT status FROM metro.tickets WHERE id=@id",("id",t.Id)));
        Assert.Equal(17999.9m,await Sql("SELECT observed_load_kg FROM metro.load_observations WHERE planned_trip_id=@id",("id",n.Trip)));
        Assert.Equal("Deny",(await Scan(staff,n,Entry(n,t.Qr))).Decision);
    }

    [PostgreSqlFact, Trait("Category","PostgreSQL")]
    public async Task Missing_full_equal_limit_and_over_limit_load_deny_without_consuming_and_latest_observation_has_no_TTL()
    {
        using var staff=await AccountAsync("StationStaff"); using var holder=await AccountAsync(); var n=await NetworkAsync(staff); var t=await Issue(n,holder);
        await Sql("DELETE FROM metro.load_observations WHERE operator_id=@op",("op",n.Op));
        Assert.Equal("LOAD_NO_DATA",(await Scan(staff,n,Entry(n,t.Qr))).ReasonCode);
        await Sql("INSERT INTO metro.load_observations(operator_id,planned_trip_id,observed_load_kg,recorded_by_user_id,observed_at) VALUES(@op,@trip,18000,@actor,clock_timestamp())",("op",n.Op),("trip",n.Trip),("actor",staff.Id));
        Assert.Equal("LOAD_FULL",(await Scan(staff,n,Entry(n,t.Qr))).ReasonCode);
        await Sql("UPDATE metro.load_observations SET observed_load_kg=18000.1 WHERE operator_id=@op",("op",n.Op));
        Assert.Equal("LOAD_FULL",(await Scan(staff,n,Entry(n,t.Qr))).ReasonCode);
        Assert.Equal(0L,await Sql("SELECT count(*) FROM metro.journeys WHERE ticket_id=@id",("id",t.Id)));
        await Sql("UPDATE metro.planned_trips SET load_profile_id=NULL WHERE operator_id=@op",("op",n.Op));
        Assert.Equal("LOAD_NO_LIMIT",(await Scan(staff,n,Entry(n,t.Qr))).ReasonCode);
        await Sql("UPDATE metro.planned_trips SET load_profile_id=@profile WHERE operator_id=@op",("op",n.Op),("profile",n.Profile));
        await Sql("UPDATE metro.load_observations SET observed_load_kg=17999.9,observed_at=clock_timestamp()-interval '10 days' WHERE operator_id=@op",("op",n.Op));
        Assert.Equal("Allow",(await Scan(staff,n,Entry(n,t.Qr))).Decision);
    }

    [PostgreSqlFact, Trait("Category","PostgreSQL")]
    public async Task Exit_ignores_expiry_holder_lock_full_load_and_trip_but_periodic_ticket_can_start_again_only_while_valid()
    {
        foreach(var product in new[]{"SINGLE_TRIP","DAY","WEEK","MONTH"})
        {
            using var staff=await AccountAsync("StationStaff"); using var holder=await AccountAsync(); var n=await NetworkAsync(staff,product); var t=await Issue(n,holder);
            Assert.Equal("Allow",(await Scan(staff,n,Entry(n,t.Qr))).Decision);
            await Sql("UPDATE metro.tickets SET expires_at=clock_timestamp()-interval '1 second',effective_from=clock_timestamp()-interval '2 days' WHERE id=@id; UPDATE metro.user_profiles SET status='LOCKED' WHERE user_id=@holder; UPDATE metro.load_observations SET observed_load_kg=18000 WHERE operator_id=@op; UPDATE metro.planned_trips SET boarding_status='CLOSED' WHERE operator_id=@op",("id",t.Id),("holder",holder.Id),("op",n.Op));
            Assert.Equal("Allow",(await Scan(staff,n,Exit(n,t.Qr))).Decision);
            Assert.Equal(product=="SINGLE_TRIP" ? "USED":"ACTIVE",await Sql("SELECT status FROM metro.tickets WHERE id=@id",("id",t.Id)));
            Assert.Equal("Deny",(await Scan(staff,n,Entry(n,t.Qr))).Decision);
        }
        using var s=await AccountAsync("StationStaff"); using var h=await AccountAsync(); var passNet=await NetworkAsync(s,"DAY"); var pass=await Issue(passNet,h);
        for(var i=0;i<2;i++) { Assert.Equal("Allow",(await Scan(s,passNet,Entry(passNet,pass.Qr))).Decision); Assert.Equal("Allow",(await Scan(s,passNet,Exit(passNet,pass.Qr))).Decision); }
        Assert.Equal(2L,await Sql("SELECT count(*) FROM metro.journeys WHERE ticket_id=@id",("id",pass.Id)));
    }

    [PostgreSqlFact, Trait("Category","PostgreSQL")]
    public async Task Wrong_station_direction_departed_train_missing_actual_arrival_and_revoked_QR_cannot_authorize_entry()
    {
        using var staff=await AccountAsync("StationStaff"); using var holder=await AccountAsync(); var n=await NetworkAsync(staff); var t=await Issue(n,holder);
        Assert.Equal("Deny",(await Scan(staff,n,Entry(n,t.Qr) with{StationId=n.B})).Decision);
        Assert.Equal("Deny",(await Scan(staff,n,Entry(n,t.Qr) with{Direction=1})).Decision);
        await Sql("UPDATE metro.actual_stop_times SET actual_arrival_at=NULL WHERE operator_id=@op",("op",n.Op));
        Assert.Equal("TRIP_NOT_BOARDING_AT_STATION",(await Scan(staff,n,Entry(n,t.Qr))).ReasonCode);
        await Sql("UPDATE metro.actual_stop_times SET actual_arrival_at=clock_timestamp()-interval '2 minutes',actual_departure_at=clock_timestamp()-interval '1 minute' WHERE operator_id=@op",("op",n.Op));
        Assert.Equal("Deny",(await Scan(staff,n,Entry(n,t.Qr))).Decision);
        await Sql("UPDATE metro.ticket_credentials SET status='REVOKED',revoked_at=clock_timestamp() WHERE ticket_id=@id",("id",t.Id));
        Assert.Equal("QR_REVOKED",(await Scan(staff,n,Entry(n,t.Qr))).ReasonCode);
        Assert.Equal("QR_INVALID",(await Scan(staff,n,Entry(n,t.Qr[..^1]+(t.Qr[^1]=='0'?'1':'0')))).ReasonCode);
        Assert.Equal(0L,await Sql("SELECT count(*) FROM metro.journeys WHERE ticket_id=@id",("id",t.Id)));
    }

    [PostgreSqlFact, Trait("Category","PostgreSQL")]
    public async Task Guardian_child_entry_and_exit_are_atomic_and_revocation_after_entry_does_not_trap_the_pair()
    {
        using var staff=await AccountAsync("StationStaff"); using var guardian=await AccountAsync(); using var child=await AccountAsync(birth:Today.AddYears(-5));
        var n=await NetworkAsync(staff); var parent=await Issue(n,guardian);
        await Sql("INSERT INTO metro.guardian_links(operator_id,guardian_user_id,child_user_id,relationship,status,verified_by_user_id,verified_at,valid_from,valid_to) VALUES(@op,@guardian,@child,'PARENT','ACTIVE',@actor,clock_timestamp(),@start,@end)",("op",n.Op),("guardian",guardian.Id),("child",child.Id),("actor",staff.Id),("start",Today),("end",Today.AddDays(30)));
        var kid=await Issue(n,child,guardian);
        Assert.Equal("GUARDIAN_PAIR_REQUIRED",(await Scan(staff,n,Entry(n,kid.Qr))).ReasonCode);
        Assert.Equal("Deny",(await Scan(staff,n,Entry(n,parent.Qr,kid.Qr[..^1]+"z"))).Decision);
        await Sql("UPDATE metro.load_observations SET observed_load_kg=18000 WHERE operator_id=@op",("op",n.Op));
        Assert.Equal("Deny",(await Scan(staff,n,Entry(n,parent.Qr,kid.Qr))).Decision);
        Assert.Equal(0L,await Sql("SELECT count(*) FROM metro.journeys WHERE operator_id=@op",("op",n.Op)));
        await Sql("UPDATE metro.load_observations SET observed_load_kg=17999.9 WHERE operator_id=@op",("op",n.Op));
        var entry=await Scan(staff,n,Entry(n,parent.Qr,kid.Qr)); Assert.Equal("Allow",entry.Decision); Assert.Equal(2,entry.Passages.Count);
        Assert.Equal("GUARDIAN_PAIR_REQUIRED",(await Scan(staff,n,Exit(n,parent.Qr))).ReasonCode);
        await Sql("UPDATE metro.guardian_links SET status='REVOKED',revocation_reason='fixture' WHERE operator_id=@op; UPDATE metro.load_observations SET observed_load_kg=18000 WHERE operator_id=@op",("op",n.Op));
        Assert.Equal("Allow",(await Scan(staff,n,Exit(n,kid.Qr,parent.Qr))).Decision);
        Assert.Equal(2L,await Sql("SELECT count(*) FROM metro.journeys WHERE operator_id=@op AND status='COMPLETED'",("op",n.Op)));
    }

    [PostgreSqlFact, Trait("Category","PostgreSQL")]
    public async Task Unknown_QR_is_durably_denied_without_fake_ticket_and_operator_boundaries_and_logs_are_private()
    {
        using var staff=await AccountAsync("StationStaff"); using var holder=await AccountAsync(); var n=await NetworkAsync(staff); var other=await NetworkAsync(staff); var t=await Issue(n,holder);
        var request=Entry(n,"not-a-ticket"); Assert.Equal("Deny",(await Scan(staff,n,request)).Decision);
        Assert.Equal("Deny",(await Scan(staff,n,request)).Decision);
        Assert.Equal(1L,await Sql("SELECT count(*) FROM metro.gate_requests WHERE operator_id=@op",("op",n.Op)));
        Assert.Equal(0L,await Sql("SELECT count(*) FROM metro.afc_events WHERE operator_id=@op",("op",n.Op)));
        var escalation=new GateResolutionRequest(Guid.NewGuid(),request.RequestId,"ESCALATE",n.A,"Unreadable QR","case-unreadable",false);
        var escalated=await Resolve(staff,n,escalation);
        Assert.Equal(HttpStatusCode.OK,escalated.StatusCode);
        Assert.Equal("NEEDS_ADMIN_SUPPORT",(await escalated.Content.ReadFromJsonAsync<GateResult>())!.ReasonCode);
        Assert.Equal(0L,await Sql("SELECT count(*) FROM metro.afc_events WHERE operator_id=@op",("op",n.Op)));
        Assert.Equal("QR_INVALID",(await Scan(staff,other,Entry(other,t.Qr))).ReasonCode);
        var code=(string)(await Sql("SELECT ticket_code FROM metro.tickets WHERE id=@id",("id",t.Id)))!;
        var lookup=await staff.Client.PostAsJsonAsync(Url(n,"lookup"),new GateLookupRequest(code,"Passenger support"));
        Assert.Equal(HttpStatusCode.OK,lookup.StatusCode); var body=await lookup.Content.ReadAsStringAsync();
        Assert.DoesNotContain("tokenHash",body); Assert.DoesNotContain("birthDate",body); Assert.DoesNotContain("payment",body,StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.NotFound,(await staff.Client.PostAsJsonAsync(Url(other,"lookup"),new GateLookupRequest(code,"Support"))).StatusCode);
        Assert.DoesNotContain(t.Qr,string.Join('\n',factory.TestLogs.Messages));
    }
}
