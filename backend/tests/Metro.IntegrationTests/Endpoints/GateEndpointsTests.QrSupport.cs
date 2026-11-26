using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Metro.Application.Abstractions.Gates;
using Metro.Infrastructure.Gates;
using Metro.Infrastructure.Ticketing;
using Metro.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Metro.IntegrationTests.Endpoints;

public sealed partial class GateEndpointsTests
{
    private static GateQrChallengeRequest Challenge(string action="REPLACE")=>new(Guid.NewGuid(),Guid.NewGuid(),action,"Gate Passenger",new(1990,1,1),"Verified passenger support");
    private static GateQrAccessRequest EmailProof(GateQrChallengeRequest r,GateQrReceipt receipt,string otp)=>
        new(Guid.NewGuid(),r.OperationId,r.Action,r.FullName,r.BirthDate,r.Reason,"EMAIL",receipt.ChallengeId,otp);
    private static GateQrAccessRequest DocumentProof(string action="REPLACE")=>new(Guid.NewGuid(),Guid.NewGuid(),action,"Gate Passenger",new(1990,1,1),
        "Verified passenger support","DOCUMENT",NoEmailAccess:true,DocumentMatchesProfile:true,EvidenceReference:"station-demo-document-case");
    private static async Task<HttpResponseMessage> QrWrite(Account actor,Network n,Guid ticket,object body,bool challenge)
    {
        var id=body is GateQrChallengeRequest c?c.RequestId:((GateQrAccessRequest)body).RequestId;
        using var request=new HttpRequestMessage(HttpMethod.Post,Url(n,$"tickets/{ticket}/{(challenge?"qr-challenges":"qr-support")}")){Content=JsonContent.Create(body,body.GetType())};
        request.Headers.Add("Idempotency-Key",id.ToString());return await actor.Client.SendAsync(request);
    }
    private static async Task<GateQrReceipt> QrReceipt(HttpResponseMessage response,HttpStatusCode expected=HttpStatusCode.OK)
    {
        Assert.True(response.StatusCode==expected,await response.Content.ReadAsStringAsync());return (await response.Content.ReadFromJsonAsync<GateQrReceipt>())!;
    }
    private async Task<int> SendMail(Network n)
    {
        await using var scope=factory.Services.CreateAsyncScope();return await scope.ServiceProvider.GetRequiredService<GateEmailProcessor>().ProcessDueAsync(n.Op);
    }
    private string Otp(GateQrReceipt receipt)=>Regex.Match(factory.Services.GetRequiredService<TestGateEmailSender>().Accepted[receipt.ChallengeId!.Value].Body,@"\b[0-9]{6}\b").Value;
    private async Task<string> CurrentQr(Network n,Guid ticket)
    {
        var id=(Guid)(await Sql("SELECT id FROM metro.ticket_credentials WHERE operator_id=@op AND ticket_id=@ticket AND status='ACTIVE' AND credential_type='QR'",("op",n.Op),("ticket",ticket)))!;
        var reference=(Guid)(await Sql("SELECT correlation_id FROM metro.tickets WHERE id=@ticket",("ticket",ticket)))!;
        var version=(int)(await Sql("SELECT version FROM metro.ticket_credentials WHERE id=@id",("id",id)))!;
        var key=(string)(await Sql("SELECT key_id FROM metro.ticket_credentials WHERE id=@id",("id",id)))!;
        return factory.Services.GetRequiredService<TicketQrCodec>().Token(n.Op,reference,id,version,key);
    }

    [PostgreSqlFact,Trait("Category","PostgreSQL")]
    public async Task Verified_email_replacement_revokes_old_QR_preserves_open_journey_and_is_idempotent()
    {
        using var staff=await AccountAsync("StationStaff");using var holder=await AccountAsync();var n=await NetworkAsync(staff);var t=await Issue(n,holder);
        var entry=await Scan(staff,n,Entry(n,t.Qr));var journey=entry.Passages[0].JourneyId;
        var end=await Sql("SELECT expires_at FROM metro.tickets WHERE id=@id",("id",t.Id));
        var r=Challenge();var c=await QrReceipt(await QrWrite(staff,n,t.Id,r,true),HttpStatusCode.Accepted);
        Assert.Null(c.CredentialId);Assert.Equal(HttpStatusCode.Forbidden,(await staff.Client.GetAsync(Url(n,$"qr-requests/{r.RequestId}/image"))).StatusCode);
        await SendMail(n);var otp=Otp(c);Assert.Equal(6,otp.Length);
        var request=EmailProof(r,c,otp);
        var replies=await Task.WhenAll(QrWrite(staff,n,t.Id,request,false),QrWrite(staff,n,t.Id,request,false));
        var first=await QrReceipt(replies[0]);var second=await QrReceipt(replies[1]);Assert.Equal(first,second);Assert.Equal(2,first.Version);
        Assert.Equal(1L,await Sql("SELECT count(*) FROM metro.ticket_credentials WHERE ticket_id=@id AND status='ACTIVE'",("id",t.Id)));
        Assert.Equal(1L,await Sql("SELECT count(*) FROM metro.ticket_credentials WHERE ticket_id=@id AND status='REPLACED' AND replaced_by_id=@current",("id",t.Id),("current",first.CredentialId)));
        Assert.Equal(end,await Sql("SELECT expires_at FROM metro.tickets WHERE id=@id",("id",t.Id)));
        Assert.Equal("IN_USE",await Sql("SELECT status FROM metro.tickets WHERE id=@id",("id",t.Id)));
        Assert.Equal("OPEN",await Sql("SELECT status FROM metro.journeys WHERE id=@id",("id",journey)));
        Assert.Equal("QR_REVOKED",(await Scan(staff,n,Exit(n,t.Qr))).ReasonCode);
        var current=await CurrentQr(n,t.Id);Assert.Equal("Allow",(await Scan(staff,n,Exit(n,current))).Decision);
        Assert.Equal(journey,await Sql("SELECT id FROM metro.journeys WHERE ticket_id=@id AND status='COMPLETED'",("id",t.Id)));
        Assert.Equal("USED",await Sql("SELECT status FROM metro.tickets WHERE id=@id",("id",t.Id)));
        Assert.DoesNotContain(otp,string.Join('\n',factory.TestLogs.Messages));Assert.DoesNotContain(current,string.Join('\n',factory.TestLogs.Messages));
    }

    [PostgreSqlFact,Trait("Category","PostgreSQL")]
    public async Task Display_keeps_the_same_QR_and_proof_is_bound_to_the_staff_session_and_current_profile()
    {
        using var staff=await AccountAsync("StationStaff");using var holder=await AccountAsync();using var other=await AccountAsync("StationStaff");
        var n=await NetworkAsync(staff);var t=await Issue(n,holder);
        await Sql("INSERT INTO metro.operator_accounts(operator_id,user_id,account_type,mfa_required) VALUES(@op,@id,'STAFF',true)",("op",n.Op),("id",other.Id));
        var r=Challenge("DISPLAY");var c=await QrReceipt(await QrWrite(staff,n,t.Id,r,true),HttpStatusCode.Accepted);await SendMail(n);
        var p=EmailProof(r,c,Otp(c));var result=await QrReceipt(await QrWrite(staff,n,t.Id,p,false));Assert.Equal(1,result.Version);
        var image=await staff.Client.GetAsync(Url(n,$"qr-requests/{p.RequestId}/image"));Assert.Equal(HttpStatusCode.OK,image.StatusCode);Assert.True(image.Headers.CacheControl?.NoStore);
        Assert.Equal(await holder.Client.GetByteArrayAsync($"/api/v1/tickets/{t.Id}/qr?operatorCode={n.Code}"),await image.Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.Forbidden,(await other.Client.GetAsync(Url(n,$"qr-requests/{p.RequestId}/image"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await other.Client.GetAsync(Url(n,$"qr-requests/{p.RequestId}"))).StatusCode);
        await Sql("UPDATE metro.user_profiles SET full_name='Changed identity' WHERE user_id=@id",("id",holder.Id));
        Assert.Equal(HttpStatusCode.Conflict,(await staff.Client.GetAsync(Url(n,$"qr-requests/{p.RequestId}/image"))).StatusCode);
        Assert.Equal(1L,await Sql("SELECT count(*) FROM metro.ticket_credentials WHERE ticket_id=@id",("id",t.Id)));
    }

    [PostgreSqlFact,Trait("Category","PostgreSQL")]
    public async Task Email_proof_cannot_be_used_for_another_ticket_action_or_after_consumption_and_bad_attempts_replay_once()
    {
        using var staff=await AccountAsync("StationStaff");using var a=await AccountAsync();using var b=await AccountAsync();var n=await NetworkAsync(staff);
        var ta=await Issue(n,a);var tb=await Issue(n,b);var r=Challenge("DISPLAY");var c=await QrReceipt(await QrWrite(staff,n,ta.Id,r,true),HttpStatusCode.Accepted);await SendMail(n);var code=Otp(c);
        var p=EmailProof(r,c,code);Assert.Equal(HttpStatusCode.Conflict,(await QrWrite(staff,n,tb.Id,p,false)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await QrWrite(staff,n,ta.Id,p with{Action="REPLACE"},false)).StatusCode);
        var wrong=p with{RequestId=Guid.NewGuid(),EmailCode=code=="000000"?"999999":"000000"};
        Assert.Equal(HttpStatusCode.BadRequest,(await QrWrite(staff,n,ta.Id,wrong,false)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await QrWrite(staff,n,ta.Id,wrong,false)).StatusCode);
        Assert.Equal((short)1,await Sql("SELECT failed_attempts FROM metro.email_verification_challenges WHERE id=@id",("id",c.ChallengeId)));
        for(var i=0;i<4;i++)Assert.Equal(HttpStatusCode.BadRequest,(await QrWrite(staff,n,ta.Id,wrong with{RequestId=Guid.NewGuid()},false)).StatusCode);
        Assert.Equal((short)5,await Sql("SELECT failed_attempts FROM metro.email_verification_challenges WHERE id=@id",("id",c.ChallengeId)));
        Assert.Equal(HttpStatusCode.BadRequest,(await QrWrite(staff,n,ta.Id,p,false)).StatusCode);
        Assert.Equal(1L,await Sql("SELECT count(*) FROM metro.ticket_credentials WHERE ticket_id=@id",("id",ta.Id)));
    }

    [PostgreSqlFact,Trait("Category","PostgreSQL")]
    public async Task Document_fallback_requires_checked_matching_identity_and_cannot_resurrect_used_or_expired_tickets()
    {
        using var staff=await AccountAsync("StationStaff");using var holder=await AccountAsync();var n=await NetworkAsync(staff);var t=await Issue(n,holder);
        var proof=DocumentProof();
        Assert.Equal(HttpStatusCode.BadRequest,(await QrWrite(staff,n,t.Id,proof with{NoEmailAccess=false},false)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await QrWrite(staff,n,t.Id,proof with{FullName="Wrong identity"},false)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await QrWrite(staff,n,t.Id,proof with{BirthDate=new(1991,1,1)},false)).StatusCode);
        var pending=new GateQrChallengeRequest(Guid.NewGuid(),proof.OperationId,proof.Action,proof.FullName,proof.BirthDate,proof.Reason);
        var cancelled=await QrReceipt(await QrWrite(staff,n,t.Id,pending,true),HttpStatusCode.Accepted);
        var result=await QrReceipt(await QrWrite(staff,n,t.Id,proof,false));Assert.Equal("QR_REPLACED",result.Code);
        Assert.NotNull(await Sql("SELECT superseded_at FROM metro.email_verification_challenges WHERE id=@id",("id",cancelled.ChallengeId)));
        Assert.Null(await Sql("SELECT protected_payload FROM metro.gate_email_outbox WHERE id=@id",("id",cancelled.ChallengeId)));
        Assert.Equal(HttpStatusCode.OK,(await staff.Client.GetAsync(Url(n,$"qr-requests/{proof.RequestId}/image"))).StatusCode);
        var current=await CurrentQr(n,t.Id);Assert.Equal("Allow",(await Scan(staff,n,Entry(n,current))).Decision);Assert.Equal("Allow",(await Scan(staff,n,Exit(n,current))).Decision);
        Assert.Equal(HttpStatusCode.Conflict,(await QrWrite(staff,n,t.Id,DocumentProof(),false)).StatusCode);
        using var expired=await AccountAsync();var old=await Issue(n,expired);
        await Sql("UPDATE metro.tickets SET effective_from=clock_timestamp()-interval '2 days',expires_at=clock_timestamp()-interval '1 day' WHERE id=@id",("id",old.Id));
        Assert.Equal(HttpStatusCode.Conflict,(await QrWrite(staff,n,old.Id,DocumentProof(),false)).StatusCode);
    }
}
