using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.Swagger;
using Metro.Application.Abstractions.Gates;
using Metro.IntegrationTests.Infrastructure;

namespace Metro.IntegrationTests.Endpoints;

public sealed partial class GateEndpointsTests
{
    [PostgreSqlFact, Trait("Category","PostgreSQL")]
    public void Swagger_documents_staff_security_idempotency_and_unconfirmed_results()
    {
        var swagger=factory.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");
        var scan=swagger.Paths["/api/v1/gate/scans"].Operations[OperationType.Post];
        Assert.NotEmpty(scan.Security);
        Assert.Contains(scan.Parameters,p=>p.Name=="Idempotency-Key" && p.Required);
        var example=Assert.IsType<OpenApiObject>(scan.Responses["202"].Content["application/json"].Example);
        Assert.Equal("Pending",Assert.IsType<OpenApiString>(example["decision"]).Value);
        foreach(var status in new[]{"200","202","400","401","403","404","409","503"}) Assert.Contains(status,scan.Responses.Keys);
        foreach(var route in new[]{"/api/v1/gate/tickets/{ticketId}/qr-challenges","/api/v1/gate/tickets/{ticketId}/qr-support",
            "/api/v1/gate/qr-requests/{requestId}","/api/v1/gate/qr-requests/{requestId}/image"})
            Assert.All(swagger.Paths[route].Operations.Values,operation=>Assert.NotEmpty(operation.Security));
        Assert.Equal("binary",swagger.Paths["/api/v1/gate/qr-requests/{requestId}/image"].Operations[OperationType.Get].Responses["200"].Content["image/png"].Schema.Format);
    }
    [PostgreSqlFact, Trait("Category","PostgreSQL")]
    public async Task Suggested_trip_is_not_a_gate_restriction_and_missing_ETA_does_not_block_real_boarding()
    {
        using var staff=await AccountAsync("StationStaff"); using var holder=await AccountAsync(); var n=await NetworkAsync(staff); var t=await Issue(n,holder);
        var b=Guid.NewGuid();var actual=Guid.NewGuid();
        await Sql("""
            INSERT INTO metro.trip_suggestions(operator_id,order_id,order_version,suggestion_code,beneficiary_user_id,service_date,origin_station_id,destination_station_id,line_id,direction,suggested_planned_trip_id)
              SELECT @op,order_id,order_version,@code,holder_user_id,service_date,origin_station_id,destination_station_id,@line,0,@a FROM metro.tickets WHERE id=@ticket;
            INSERT INTO metro.planned_trips(id,operator_id,schedule_version_id,line_id,trip_code,service_date,direction,planned_start_at,planned_end_at,status,load_profile_id,boarding_station_id)
              SELECT @b,operator_id,schedule_version_id,line_id,'B',service_date,direction,planned_start_at,planned_end_at,'DELAYED',load_profile_id,boarding_station_id FROM metro.planned_trips WHERE id=@a;
            INSERT INTO metro.planned_stop_times(operator_id,planned_trip_id,station_id,sequence_no,planned_arrival_at,planned_departure_at)
              SELECT operator_id,@b,station_id,sequence_no,planned_arrival_at,planned_departure_at FROM metro.planned_stop_times WHERE planned_trip_id=@a;
            INSERT INTO metro.actual_trips(id,operator_id,planned_trip_id,status,source) VALUES(@actual,@op,@b,'DELAYED','SIMULATION');
            INSERT INTO metro.actual_stop_times(operator_id,actual_trip_id,station_id,sequence_no,actual_arrival_at,source)
              VALUES(@op,@actual,@origin,1,clock_timestamp()-interval '1 minute','SIMULATION');
            INSERT INTO metro.load_observations(operator_id,planned_trip_id,observed_load_kg,recorded_by_user_id,observed_at)
              VALUES(@op,@b,17999.9,@actor,clock_timestamp());
            """,("op",n.Op),("code",Guid.NewGuid().ToString()),("line",n.Line),("a",n.Trip),("b",b),("ticket",t.Id),("actual",actual),("origin",n.A),("actor",staff.Id));
        var r=await Scan(staff,n,Entry(n,t.Qr) with{PlannedTripId=b});Assert.Equal("Allow",r.Decision);
        Assert.Equal(b,await Sql("SELECT entry_planned_trip_id FROM metro.journeys WHERE id=@id",("id",r.Passages[0].JourneyId)));
        Assert.Equal("Allow",(await Scan(staff,n,Exit(n,t.Qr))).Decision);
    }

    [PostgreSqlFact, Trait("Category","PostgreSQL")]
    public async Task Frozen_first_leg_controls_transfer_entry_without_requiring_destination_on_the_first_train()
    {
        using var staff=await AccountAsync("StationStaff"); using var holder=await AccountAsync(); var n=await NetworkAsync(staff);
        var t=await Issue(n,holder,destination:n.C,snapshot:new{FirstLineId=n.Line,FirstDirection=0,FirstLegEnd=n.B});
        await Sql("DELETE FROM metro.planned_stop_times WHERE planned_trip_id=@trip AND station_id=@dest",("dest",n.C),("trip",n.Trip));
        Assert.Equal("Allow",(await Scan(staff,n,Entry(n,t.Qr))).Decision);
        Assert.Equal("Allow",(await Scan(staff,n,Exit(n,t.Qr) with{StationId=n.C})).Decision);
    }

    [PostgreSqlFact, Trait("Category","PostgreSQL")]
    public async Task Guardian_pair_correction_is_atomic_and_an_expired_open_journey_never_auto_closes()
    {
        using var staff=await AccountAsync("StationStaff");using var guardian=await AccountAsync();using var child=await AccountAsync(birth:Today.AddYears(-5));
        var n=await NetworkAsync(staff);var parent=await Issue(n,guardian);
        await Sql("INSERT INTO metro.guardian_links(operator_id,guardian_user_id,child_user_id,relationship,status,verified_by_user_id,verified_at) VALUES(@op,@g,@c,'PARENT','ACTIVE',@actor,clock_timestamp())",("op",n.Op),("g",guardian.Id),("c",child.Id),("actor",staff.Id));
        var kid=await Issue(n,child,guardian);var entry=Entry(n,parent.Qr,kid.Qr);Assert.Equal("Allow",(await Scan(staff,n,entry)).Decision);
        var undo=new GateResolutionRequest(Guid.NewGuid(),entry.RequestId,"UNDO_SCAN",n.A,"Pair scan mistake","pair-evidence",true,PairPresentConfirmed:true,PassengerHasNotTravelled:true);
        var reply=await Resolve(staff,n,undo);Assert.True(reply.IsSuccessStatusCode,await reply.Content.ReadAsStringAsync());
        Assert.Equal(2L,await Sql("SELECT count(*) FROM metro.tickets WHERE operator_id=@op AND status='ACTIVE'",("op",n.Op)));
        Assert.Equal(2L,await Sql("SELECT count(*) FROM metro.journeys WHERE operator_id=@op AND status='CANCELLED'",("op",n.Op)));
        Assert.Equal("Allow",(await Scan(staff,n,Entry(n,parent.Qr,kid.Qr))).Decision);
        await Sql("UPDATE metro.tickets SET effective_from=clock_timestamp()-interval '2 days',expires_at=clock_timestamp()-interval '1 day' WHERE operator_id=@op",("op",n.Op));
        Assert.Equal("Deny",(await Scan(staff,n,Entry(n,parent.Qr,kid.Qr))).Decision);
        Assert.Equal(2L,await Sql("SELECT count(*) FROM metro.journeys WHERE operator_id=@op AND status='OPEN'",("op",n.Op)));
        Assert.Equal("Allow",(await Scan(staff,n,Exit(n,parent.Qr,kid.Qr))).Decision);
    }
}
