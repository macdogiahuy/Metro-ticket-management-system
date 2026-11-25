using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Metro.Application.Abstractions.Gates;
using Metro.Infrastructure.Gates;
using Metro.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Metro.IntegrationTests.Endpoints;

public sealed partial class GateEndpointsTests
{
    [PostgreSqlFact,Trait("Category","PostgreSQL")]
    public async Task Email_code_expiring_while_waiting_for_the_holder_lock_is_not_consumed_or_used_to_replace_QR()
    {
        using var staff=await AccountAsync("StationStaff");using var holder=await AccountAsync();var n=await NetworkAsync(staff);var t=await Issue(n,holder);
        var r=Challenge();var proof=await QrReceipt(await QrWrite(staff,n,t.Id,r,true),HttpStatusCode.Accepted);await SendMail(n);var otp=Otp(proof);
        await using var c=new NpgsqlConnection(PostgreSqlTestConfiguration.LoadRequired().ConnectionString);await c.OpenAsync();
        await using var tx=await c.BeginTransactionAsync();
        await using(var lockHolder=new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@op::text||':'||@holder,0))",c,tx))
        {
            lockHolder.Parameters.AddWithValue("op",n.Op);lockHolder.Parameters.AddWithValue("holder",holder.Id);await lockHolder.ExecuteNonQueryAsync();
        }
        await Sql("UPDATE metro.email_verification_challenges SET created_at=statement_timestamp()-interval '299.25 seconds',expires_at=statement_timestamp()+interval '0.75 seconds' WHERE id=@id",("id",proof.ChallengeId));
        var pending=QrWrite(staff,n,t.Id,EmailProof(r,proof,otp),false);
        await Task.Delay(TimeSpan.FromMilliseconds(1100));
        Assert.False(pending.IsCompleted);await tx.RollbackAsync();
        Assert.Equal(HttpStatusCode.BadRequest,(await pending).StatusCode);
        Assert.Null(await Sql("SELECT consumed_at FROM metro.email_verification_challenges WHERE id=@id",("id",proof.ChallengeId)));
        Assert.Equal(1L,await Sql("SELECT count(*) FROM metro.ticket_credentials WHERE ticket_id=@id AND status='ACTIVE'",("id",t.Id)));
        Assert.Equal(1L,await Sql("SELECT count(*) FROM metro.ticket_credentials WHERE ticket_id=@id",("id",t.Id)));
    }
    [PostgreSqlFact,Trait("Category","PostgreSQL")]
    public async Task QR_support_routes_enforce_401_403_operator_scope_and_never_return_codes_even_in_test_mode()
    {
        using var staff=await AccountAsync("StationStaff");using var holder=await AccountAsync();var n=await NetworkAsync(staff);var other=await NetworkAsync(staff);var t=await Issue(n,holder);
        using var anonymous=factory.CreateClient();
        foreach(var client in new[]{anonymous,holder.Client})
        {
            var expected=client==anonymous?HttpStatusCode.Unauthorized:HttpStatusCode.Forbidden;
            foreach(var path in new[]{$"tickets/{t.Id}/qr-challenges",$"tickets/{t.Id}/qr-support"})
            {
                var body=path.EndsWith("qr-challenges",StringComparison.Ordinal)?(object)Challenge():DocumentProof();
                var id=body is GateQrChallengeRequest c?c.RequestId:((GateQrAccessRequest)body).RequestId;
                using var request=new HttpRequestMessage(HttpMethod.Post,Url(n,path)){Content=JsonContent.Create(body,body.GetType())};request.Headers.Add("Idempotency-Key",id.ToString());
                Assert.Equal(expected,(await client.SendAsync(request)).StatusCode);
            }
            foreach(var path in new[]{$"qr-requests/{Guid.NewGuid()}",$"qr-requests/{Guid.NewGuid()}/image"})
                Assert.Equal(expected,(await client.GetAsync(Url(n,path))).StatusCode);
        }
        Assert.Equal(HttpStatusCode.NotFound,(await QrWrite(staff,other,t.Id,Challenge(),true)).StatusCode);
        var r=Challenge();var c1=await QrReceipt(await QrWrite(staff,n,t.Id,r,true),HttpStatusCode.Accepted);
        var c2=await QrReceipt(await QrWrite(staff,n,t.Id,r,true),HttpStatusCode.Accepted);Assert.Equal(c1,c2);
        Assert.Equal(1L,await Sql("SELECT count(*) FROM metro.email_verification_challenges c JOIN metro.gate_qr_challenges g ON g.challenge_id=c.id WHERE g.operator_id=@op",("op",n.Op)));
        await SendMail(n);var otp=Otp(c1);
        var status=await staff.Client.GetStringAsync(Url(n,$"qr-requests/{r.RequestId}"));Assert.DoesNotContain(otp,status);
        Assert.DoesNotContain("codeHash",status,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("fullName",status,StringComparison.OrdinalIgnoreCase);
        Assert.Null(await Sql("SELECT protected_payload FROM metro.gate_email_outbox WHERE challenge_id=@id",("id",c1.ChallengeId)));
        Assert.Equal("ACCEPTED",(await staff.Client.GetFromJsonAsync<GateQrReceipt>(Url(n,$"qr-requests/{r.RequestId}")))!.DeliveryStatus);
        // An email code for one successful action is one-use, even for the same authorized employee.
        var p=EmailProof(r,c1,otp);await QrReceipt(await QrWrite(staff,n,t.Id,p,false));
        Assert.Equal(HttpStatusCode.Conflict,(await QrWrite(staff,n,t.Id,p with{RequestId=Guid.NewGuid()},false)).StatusCode);
    }

    [PostgreSqlFact,Trait("Category","PostgreSQL")]
    public async Task Email_code_limits_are_shared_with_signup_reset_and_across_devices()
    {
        using var staff=await AccountAsync("StationStaff");using var holder=await AccountAsync();var n=await NetworkAsync(staff);var t=await Issue(n,holder);
        await Sql("""
            INSERT INTO metro.email_verification_challenges(user_id,purpose,code_hash,created_at,expires_at)
              VALUES(@holder,'VERIFY_EMAIL',@hash,statement_timestamp()-interval '180 seconds',statement_timestamp()-interval '180 seconds'+interval '5 minutes');
            INSERT INTO metro.email_verification_challenges(user_id,purpose,code_hash,created_at,expires_at)
              VALUES(@holder,'RESET_PASSWORD',@hash,statement_timestamp()-interval '110 seconds',statement_timestamp()-interval '110 seconds'+interval '5 minutes')
            """,("holder",holder.Id),("hash",new string('a',64)));
        var r=Challenge();await QrReceipt(await QrWrite(staff,n,t.Id,r,true),HttpStatusCode.Accepted);
        Assert.Equal(HttpStatusCode.TooManyRequests,(await QrWrite(staff,n,t.Id,Challenge(),true)).StatusCode);
        // Advance only the test timestamps; the production policy uses the shared database clock.
        await Sql("UPDATE metro.email_verification_challenges SET created_at=created_at-interval '61 seconds',expires_at=expires_at-interval '61 seconds' WHERE user_id=@holder",("holder",holder.Id));
        Assert.Equal(HttpStatusCode.TooManyRequests,(await QrWrite(staff,n,t.Id,Challenge(),true)).StatusCode);
        Assert.Equal(3L,await Sql("SELECT count(*) FROM metro.email_verification_challenges WHERE user_id=@holder",("holder",holder.Id)));
        Assert.Equal(0L,await Sql("SELECT count(*) FROM metro.email_verification_challenges WHERE user_id=@holder AND purpose IN ('VERIFY_EMAIL','RESET_PASSWORD') AND superseded_at IS NOT NULL",("holder",holder.Id)));
    }

    [PostgreSqlFact,Trait("Category","PostgreSQL")]
    public async Task A_new_QR_support_code_supersedes_only_its_purpose_and_rejects_changed_profile_or_wrong_purpose()
    {
        using var staff=await AccountAsync("StationStaff");using var holder=await AccountAsync();var n=await NetworkAsync(staff);var t=await Issue(n,holder);
        var signup=Guid.NewGuid();
        await Sql("INSERT INTO metro.email_verification_challenges(id,user_id,purpose,code_hash,created_at,expires_at) VALUES(@id,@holder,'VERIFY_EMAIL',@hash,statement_timestamp()-interval '2 minutes',statement_timestamp()+interval '3 minutes')",("id",signup),("holder",holder.Id),("hash",new string('b',64)));
        var r=Challenge("DISPLAY");var first=await QrReceipt(await QrWrite(staff,n,t.Id,r,true),HttpStatusCode.Accepted);await SendMail(n);var code=Otp(first);
        Assert.Equal(HttpStatusCode.BadRequest,(await QrWrite(staff,n,t.Id,EmailProof(r,first,code) with{ChallengeId=signup},false)).StatusCode);
        await Sql("UPDATE metro.email_verification_challenges SET created_at=created_at-interval '61 seconds',expires_at=expires_at-interval '61 seconds' WHERE user_id=@id",("id",holder.Id));
        var newer=r with{RequestId=Guid.NewGuid()};var second=await QrReceipt(await QrWrite(staff,n,t.Id,newer,true),HttpStatusCode.Accepted);
        Assert.NotNull(await Sql("SELECT superseded_at FROM metro.email_verification_challenges WHERE id=@id",("id",first.ChallengeId)));
        Assert.Null(await Sql("SELECT superseded_at FROM metro.email_verification_challenges WHERE id=@id",("id",signup)));
        Assert.Equal(HttpStatusCode.BadRequest,(await QrWrite(staff,n,t.Id,EmailProof(r,first,code),false)).StatusCode);
        await SendMail(n);var current=Otp(second);
        await Sql("UPDATE metro.user_profiles SET birth_date='1991-01-01'::date WHERE user_id=@id",("id",holder.Id));
        Assert.Equal(HttpStatusCode.BadRequest,(await QrWrite(staff,n,t.Id,EmailProof(newer,second,current),false)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await QrWrite(staff,n,t.Id,EmailProof(newer,second,current) with{BirthDate=new(1991,1,1)},false)).StatusCode);
        Assert.Null(await Sql("SELECT consumed_at FROM metro.email_verification_challenges WHERE id=@id",("id",second.ChallengeId)));
    }

    [PostgreSqlFact,Trait("Category","PostgreSQL")]
    public async Task Failed_email_retries_the_same_encrypted_code_once_without_extending_expiry_and_expired_payload_is_erased()
    {
        using var staff=await AccountAsync("StationStaff");using var holder=await AccountAsync();var n=await NetworkAsync(staff);var t=await Issue(n,holder);
        var r=Challenge();var c=await QrReceipt(await QrWrite(staff,n,t.Id,r,true),HttpStatusCode.Accepted);
        var payload=(string)(await Sql("SELECT protected_payload FROM metro.gate_email_outbox WHERE id=@id",("id",c.ChallengeId)))!;
        Assert.DoesNotContain("Mã xác minh",payload);var mailbox=factory.Services.GetRequiredService<TestGateEmailSender>();mailbox.FailuresRemaining[c.ChallengeId!.Value]=2;
        var expires=await Sql("SELECT expires_at FROM metro.email_verification_challenges WHERE id=@id",("id",c.ChallengeId));
        await SendMail(n);Assert.Equal(1,mailbox.Calls[c.ChallengeId.Value]);await SendMail(n);Assert.Equal(1,mailbox.Calls[c.ChallengeId.Value]);
        await Sql("UPDATE metro.gate_email_outbox SET next_attempt_at=clock_timestamp()-interval '1 second' WHERE id=@id",("id",c.ChallengeId));await SendMail(n);await SendMail(n);
        Assert.Equal(2,mailbox.Calls[c.ChallengeId.Value]);Assert.Equal("FAILED",await Sql("SELECT status FROM metro.gate_email_outbox WHERE id=@id",("id",c.ChallengeId)));
        Assert.Null(await Sql("SELECT protected_payload FROM metro.gate_email_outbox WHERE id=@id",("id",c.ChallengeId)));
        Assert.Equal(expires,await Sql("SELECT expires_at FROM metro.email_verification_challenges WHERE id=@id",("id",c.ChallengeId)));
        Assert.Equal(1L,await Sql("SELECT count(*) FROM metro.email_verification_challenges WHERE user_id=@id",("id",holder.Id)));
        using var other=await AccountAsync();var ot=await Issue(n,other);var old=Challenge();var expired=await QrReceipt(await QrWrite(staff,n,ot.Id,old,true),HttpStatusCode.Accepted);
        await Sql("UPDATE metro.email_verification_challenges SET created_at=statement_timestamp()-interval '6 minutes',expires_at=statement_timestamp()-interval '1 minute' WHERE id=@id; UPDATE metro.gate_email_outbox SET expires_at=clock_timestamp()-interval '1 minute' WHERE id=@id",("id",expired.ChallengeId));
        await SendMail(n);Assert.False(mailbox.Calls.ContainsKey(expired.ChallengeId!.Value));
        Assert.Null(await Sql("SELECT protected_payload FROM metro.gate_email_outbox WHERE id=@id",("id",expired.ChallengeId)));
        Assert.Equal("EXPIRED",await Sql("SELECT status FROM metro.gate_email_outbox WHERE id=@id",("id",expired.ChallengeId)));
        Assert.Equal(HttpStatusCode.BadRequest,(await QrWrite(staff,n,ot.Id,EmailProof(old,expired,"000000"),false)).StatusCode);
    }

    [PostgreSqlFact,Trait("Category","PostgreSQL")]
    public async Task Replacing_QR_during_expired_locked_journey_keeps_exit_rights_and_notifies_the_current_guardian_only()
    {
        using var staff=await AccountAsync("StationStaff");using var guardian=await AccountAsync();using var child=await AccountAsync(birth:Today.AddYears(-5));
        var n=await NetworkAsync(staff);var parent=await Issue(n,guardian);
        await Sql("INSERT INTO metro.guardian_links(operator_id,guardian_user_id,child_user_id,relationship,status,verified_by_user_id,verified_at) VALUES(@op,@g,@c,'PARENT','ACTIVE',@actor,clock_timestamp())",("op",n.Op),("g",guardian.Id),("c",child.Id),("actor",staff.Id));
        var kid=await Issue(n,child,guardian);var entered=await Scan(staff,n,Entry(n,parent.Qr,kid.Qr));Assert.Equal("Allow",entered.Decision);
        await Sql("UPDATE metro.tickets SET effective_from=clock_timestamp()-interval '2 days',expires_at=clock_timestamp()-interval '1 day' WHERE operator_id=@op; UPDATE metro.user_profiles SET status='LOCKED' WHERE user_id=@holder",("op",n.Op),("holder",child.Id));
        var expiry=await Sql("SELECT expires_at FROM metro.tickets WHERE id=@id",("id",kid.Id));
        var proof=DocumentProof() with{BirthDate=Today.AddYears(-5)};await QrReceipt(await QrWrite(staff,n,kid.Id,proof,false));
        Assert.Equal(expiry,await Sql("SELECT expires_at FROM metro.tickets WHERE id=@id",("id",kid.Id)));
        Assert.Equal(4L,await Sql("SELECT count(*) FROM metro.notifications WHERE operator_id=@op AND notification_type='TICKET_QR_CHANGED'",("op",n.Op)));
        Assert.Equal(2L,await Sql("SELECT count(DISTINCT recipient_user_id) FROM metro.notifications WHERE operator_id=@op AND notification_type='TICKET_QR_CHANGED' AND recipient_user_id IN (@g,@c)",("op",n.Op),("g",guardian.Id),("c",child.Id)));
        Assert.Equal("Allow",(await Scan(staff,n,Exit(n,parent.Qr,await CurrentQr(n,kid.Id)))).Decision);
        Assert.Equal(2L,await Sql("SELECT count(*) FROM metro.journeys WHERE operator_id=@op AND status='COMPLETED'",("op",n.Op)));
    }

    [PostgreSqlFact,Trait("Category","PostgreSQL")]
    public async Task QR_replacement_audit_failure_rolls_back_consumption_revocation_new_code_and_notifications()
    {
        using var staff=await AccountAsync("StationStaff");using var holder=await AccountAsync();var n=await NetworkAsync(staff);var t=await Issue(n,holder);
        var r=Challenge();var c=await QrReceipt(await QrWrite(staff,n,t.Id,r,true),HttpStatusCode.Accepted);await SendMail(n);var p=EmailProof(r,c,Otp(c));
        var trigger="be10_qr_fail_"+Guid.NewGuid().ToString("N");
        await Sql($"CREATE FUNCTION metro.{trigger}() RETURNS trigger LANGUAGE plpgsql AS $f$ BEGIN RAISE EXCEPTION 'test QR audit unavailable'; END $f$; CREATE TRIGGER {trigger} BEFORE INSERT ON metro.audit_logs FOR EACH ROW WHEN(NEW.operator_id='{n.Op}'::uuid AND NEW.action='REPLACE_TICKET_QR') EXECUTE FUNCTION metro.{trigger}()");
        try
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable,(await QrWrite(staff,n,t.Id,p,false)).StatusCode);
            Assert.Null(await Sql("SELECT consumed_at FROM metro.email_verification_challenges WHERE id=@id",("id",c.ChallengeId)));
            Assert.Equal(1L,await Sql("SELECT count(*) FROM metro.ticket_credentials WHERE ticket_id=@id AND status='ACTIVE'",("id",t.Id)));
            Assert.Equal(1L,await Sql("SELECT count(*) FROM metro.ticket_credentials WHERE ticket_id=@id",("id",t.Id)));
            Assert.Equal(0L,await Sql("SELECT count(*) FROM metro.notifications WHERE operator_id=@op AND notification_type='TICKET_QR_CHANGED'",("op",n.Op)));
        }
        finally{await Sql($"DROP TRIGGER {trigger} ON metro.audit_logs; DROP FUNCTION metro.{trigger}()");}
        await QrReceipt(await QrWrite(staff,n,t.Id,p,false));
        Assert.NotNull(await Sql("SELECT consumed_at FROM metro.email_verification_challenges WHERE id=@id",("id",c.ChallengeId)));
        Assert.Equal(2L,await Sql("SELECT count(*) FROM metro.ticket_credentials WHERE ticket_id=@id",("id",t.Id)));
    }
}
