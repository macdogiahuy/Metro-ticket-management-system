using System.Net;
using System.Net.Http.Json;
using Metro.Application.Abstractions.Gates;
using Metro.IntegrationTests.Infrastructure;

namespace Metro.IntegrationTests.Endpoints;

public sealed partial class GateEndpointsTests
{
    [PostgreSqlFact,Trait("Category","PostgreSQL")]
    public async Task Escalated_cases_reach_the_operator_admin_and_can_be_closed_without_granting_passage()
    {
        using var staff=await AccountAsync("StationStaff");using var admin=await AccountAsync("Admin");using var passenger=await AccountAsync();
        var n=await NetworkAsync(staff);await Sql("INSERT INTO metro.operator_accounts(operator_id,user_id,account_type,mfa_required) VALUES(@op,@id,'ADMIN',true)",("op",n.Op),("id",admin.Id));
        using var anonymous=factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.GetAsync(Url(n,"escalations"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await staff.Client.GetAsync(Url(n,"escalations"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await passenger.Client.GetAsync(Url(n,"escalations"))).StatusCode);
        var original=Entry(n,"unreadable-code");Assert.Equal("Deny",(await Scan(staff,n,original)).Decision);
        var pending=new GateResolutionRequest(Guid.NewGuid(),original.RequestId,"ESCALATE",n.A,"Cannot verify at station","case-review-reference",false);
        Assert.Equal(HttpStatusCode.OK,(await Resolve(staff,n,pending)).StatusCode);
        Assert.Equal(1L,await Sql("SELECT count(*) FROM metro.notifications WHERE operator_id=@op AND notification_type='GATE_SUPPORT_REVIEW' AND recipient_user_id=@admin",("op",n.Op),("admin",admin.Id)));
        var page=await admin.Client.GetFromJsonAsync<GateEscalationPage>(Url(n,"escalations"));Assert.Equal(1,page!.Total);Assert.Equal(pending.RequestId,Assert.Single(page.Items).RequestId);
        var close=new GateResolutionRequest(Guid.NewGuid(),original.RequestId,"CLOSE_SUPPORT",n.A,"Reviewed; no passage authorized","reviewed-case-reference",false);
        Assert.Equal(HttpStatusCode.Forbidden,(await Resolve(staff,n,close)).StatusCode);
        var reply=await Resolve(admin,n,close);Assert.Equal(HttpStatusCode.OK,reply.StatusCode);Assert.Equal("Deny",(await reply.Content.ReadFromJsonAsync<GateResult>())!.Decision);
        Assert.Equal(0,(await admin.Client.GetFromJsonAsync<GateEscalationPage>(Url(n,"escalations")))!.Total);
        Assert.Equal(1,(await admin.Client.GetFromJsonAsync<GateEscalationPage>(Url(n,"escalations")+"&status=CLOSED"))!.Total);
        Assert.Equal(0L,await Sql("SELECT count(*) FROM metro.journeys WHERE operator_id=@op",("op",n.Op)));
        var other=await NetworkAsync(admin);Assert.Equal(0,(await admin.Client.GetFromJsonAsync<GateEscalationPage>(Url(other,"escalations")))!.Total);
    }
}
