using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Metro.Application.Abstractions.Auth;
using Metro.Application.Abstractions.Gates;
using Metro.Application.Abstractions.Ticketing;
using Metro.Application.Gates;
using Metro.Infrastructure.Persistence.Context;
using Metro.Infrastructure.Ticketing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Metro.Infrastructure.Gates;

public sealed partial class GateQrSupportService(ApplicationDbContext db, TicketQrCodec qr,
    GateEmailProtection protection, IOptions<JwtOptions> jwt) : IGateQrSupportService
{
    private async Task<T> Run<T>(GateQrActor actor, string? code, Guid request, Func<GateStore,Task<T>> action, CancellationToken ct)
    {
        if (actor.SessionId==Guid.Empty || string.IsNullOrWhiteSpace(code) || code.Length>60)
            throw new GateException("INVALID_QR_SUPPORT_CONTEXT",400);
        try
        {
            await using var c=new NpgsqlConnection(db.Database.GetConnectionString());await c.OpenAsync(ct);
            await using var tx=await c.BeginTransactionAsync(ct);
            await using var find=new NpgsqlCommand("SELECT id FROM metro.operators WHERE code=@code AND status='ACTIVE'",c,tx);
            find.Parameters.AddWithValue("code",code);
            var op=await find.ExecuteScalarAsync(ct) as Guid? ?? throw new GateException("OPERATOR_NOT_FOUND",404);
            var s=new GateStore(c,tx,op,actor.UserId);
            await s.Execute("""
                SET LOCAL lock_timeout='5s';
                SELECT pg_advisory_xact_lock_shared(73190624011222);
                SELECT pg_advisory_xact_lock_shared(hashtextextended(@actor,4));
                SELECT pg_advisory_xact_lock_shared(hashtextextended(@op::text,14));
                SELECT pg_advisory_xact_lock(hashtextextended(@op::text||':qr-support:'||@request::text,0));
                """,ct,("request",request));
            if(!await s.Staff(ct) || await s.Scalar("SELECT EXISTS(SELECT 1 FROM metro.auth_sessions WHERE id=@session AND user_id=@actor AND revoked_at IS NULL AND expires_at>clock_timestamp())",ct,("session",actor.SessionId)) is not true)
                throw new GateException("GATE_STAFF_REQUIRED",403);
            s.Now=new DateTimeOffset((DateTime)(await s.Scalar("SELECT clock_timestamp()",ct))!);
            var result=await action(s);await tx.CommitAsync(ct);return result;
        }
        catch(PostgresException e) when(e.SqlState=="P0001" && e.MessageText=="EMAIL_CODE_RATE_LIMIT") {throw new GateException("EMAIL_CODE_RATE_LIMIT",429);}
        catch(PostgresException e) when(e.SqlState is "40001" or "40P01" or "55P03" or "57014") {throw new GateException("QR_SUPPORT_UNCONFIRMED",503);}
        catch(PostgresException e) {throw new GateException(e.SqlState is "23505" or "23P01" ? "QR_SUPPORT_CONFLICT":"QR_SUPPORT_UNAVAILABLE",e.SqlState is "23505" or "23P01" ? 409:503);}
        catch(NpgsqlException) {throw new GateException("QR_SUPPORT_UNCONFIRMED",503);}
        catch(TimeoutException) {throw new GateException("QR_SUPPORT_UNCONFIRMED",503);}
        catch(TicketException e) {throw new GateException(e.Code,e.Status);}
    }
    private async Task<GateQrReceipt> Write(GateQrActor actor,string? code,Guid ticket,Guid request,Guid operation,object body,string kind,
        Func<GateStore,Task<GateQrReceipt>> action,CancellationToken ct)
    {
        GateQrReceipt result;
        try
        {
            result=await Run(actor,code,request,async s=>
            {
                var saved=await Receipt(s,actor,request,ct);
                if(saved is not null)
                {
                    if(saved.Hash!=RequestHash(ticket,body))throw new GateException("IDEMPOTENCY_KEY_CONFLICT",409);
                    return JsonSerializer.Deserialize<GateQrReceipt>(saved.Response,GateStore.Json)!;
                }
                var response=await action(s);
                await s.Execute("""
                    INSERT INTO metro.gate_qr_requests(operator_id,request_id,operation_id,actor_user_id,session_id,kind,status,payload_hash,response,created_at)
                    VALUES(@op,@request,@operation,@actor,@session,@kind,@status,@hash,@response::jsonb,@now)
                    """,ct,("request",request),("operation",operation),("session",actor.SessionId),("kind",kind),("status",response.Status),
                    ("hash",RequestHash(ticket,body)),("response",JsonSerializer.Serialize(response,GateStore.Json)));
                if(response.Status=="DENIED")await Audit(s,ticket,request,"QR_SUPPORT_DENIED",response.Code,ct);
                return response;
            },ct);
        }
        catch(GateException e) when(e.Code=="QR_SUPPORT_UNCONFIRMED")
        {return new(request,operation,ticket,"Pending",e.Code);}
        // Failed OTP attempts are durable and replayable, even though HTTP reports an error.
        if(result.Status=="DENIED")throw new GateException(result.Code,400);
        return result;
    }
    private static async Task<Saved?> Receipt(GateStore s,GateQrActor actor,Guid request,CancellationToken ct)
    {
        var row=(await s.Rows<Saved>("""
            SELECT actor_user_id AS "Actor",session_id AS "Session",payload_hash AS "Hash",response::text AS "Response"
            FROM metro.gate_qr_requests WHERE operator_id=@op AND request_id=@request
            """,ct,("request",request))).SingleOrDefault();
        if(row is not null && (row.Actor!=actor.UserId || row.Session!=actor.SessionId))throw new GateException("QR_SUPPORT_FORBIDDEN",403);
        return row;
    }
    private static async Task<SupportTicket> Ticket(GateStore s,Guid id,CancellationToken ct)
    {
        const string select="""
            SELECT t.id AS "Id",t.ticket_code AS "Code",t.correlation_id AS "Reference",t.holder_user_id AS "Holder",t.status AS "Status",
             t.effective_from AS "Start",t.expires_at AS "End",u."Email" AS "Email",u."EmailConfirmed" AS "EmailConfirmed",u."SecurityStamp" AS "Stamp",
             p.full_name AS "Name",p.birth_date AS "Birth",c.id AS "Credential",c.version AS "Version",c.key_id AS "KeyId",c.token_hash AS "Hash",
             EXISTS(SELECT 1 FROM metro.journeys j WHERE j.operator_id=t.operator_id AND j.ticket_id=t.id AND j.status IN ('OPEN','MANUAL_REVIEW') AND j.entered_at IS NOT NULL) AS "InJourney"
            FROM metro.tickets t JOIN public."AspNetUsers" u ON u."Id"=t.holder_user_id JOIN metro.user_profiles p ON p.user_id=u."Id"
             JOIN metro.ticket_credentials c ON c.operator_id=t.operator_id AND c.ticket_id=t.id AND c.credential_type='QR' AND c.status='ACTIVE' AND c.key_id IS NOT NULL
            WHERE t.operator_id=@op AND t.id=@ticket
            """;
        var t=(await s.Rows<SupportTicket>(select,ct,("ticket",id))).SingleOrDefault()??throw new GateException("TICKET_QR_NOT_READY",404);
        await s.LockHolders([t.Holder],ct);
        await s.Execute("SELECT id FROM metro.tickets WHERE operator_id=@op AND id=@ticket FOR UPDATE",ct,("ticket",id));
        s.Now=new DateTimeOffset((DateTime)(await s.Scalar("SELECT clock_timestamp()",ct))!);
        t=(await s.Rows<SupportTicket>(select,ct,("ticket",id))).SingleOrDefault()??throw new GateException("TICKET_QR_NOT_READY",409);
        if(t.Status is not ("ACTIVE" or "IN_USE") || !(t.Start<=s.Now && t.End>s.Now || t.Status=="IN_USE" && t.InJourney))
            throw new GateException("TICKET_QR_NOT_USABLE",409);
        return t;
    }
    private static string IdentityHash(SupportTicket t)=>GateStore.Hash(new{t.Id,t.Holder,t.Email,t.Stamp,Name=GateQrRules.NormalizeName(t.Name),t.Birth});
    private static void Identity(SupportTicket t,string name,DateOnly birth)
    {
        if(GateQrRules.NormalizeName(name)!=GateQrRules.NormalizeName(t.Name) || birth!=t.Birth)throw new GateException("QR_IDENTITY_MISMATCH",400);
    }
    private static async Task Intent(GateStore s,GateQrActor actor,SupportTicket t,Guid operation,string action,string reason,CancellationToken ct)
    {
        await s.Execute("""
            INSERT INTO metro.gate_qr_intents(operator_id,id,ticket_id,actor_user_id,session_id,action,profile_hash,reason_hash,credential_id)
            VALUES(@op,@id,@ticket,@actor,@session,@action,@profile,@reason,@credential) ON CONFLICT DO NOTHING
            """,ct,("id",operation),("ticket",t.Id),("session",actor.SessionId),("action",action),("profile",IdentityHash(t)),("reason",GateStore.Hash(reason)),("credential",t.Credential));
        if(await s.Scalar("""
            SELECT EXISTS(SELECT 1 FROM metro.gate_qr_intents WHERE operator_id=@op AND id=@id AND ticket_id=@ticket AND actor_user_id=@actor
              AND session_id=@session AND action=@action AND profile_hash=@profile AND reason_hash=@reason AND credential_id=@credential)
            """,ct,("id",operation),("ticket",t.Id),("session",actor.SessionId),("action",action),("profile",IdentityHash(t)),("reason",GateStore.Hash(reason)),("credential",t.Credential)) is not true)
            throw new GateException("QR_SUPPORT_CONTEXT_CHANGED",409);
        if(await s.Scalar("SELECT EXISTS(SELECT 1 FROM metro.gate_qr_requests WHERE operator_id=@op AND operation_id=@id AND kind='ACCESS' AND status='READY')",ct,("id",operation)) is true)
            throw new GateException("QR_OPERATION_ALREADY_COMPLETED",409);
    }
    private string CodeHash(Guid challenge,string code)=>Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(jwt.Value.SigningKey),Encoding.UTF8.GetBytes($"TicketQrSupport:{challenge:N}:{code}")));
    // A plain hash of an OTP-bearing request would provide an offline six-digit guessing oracle.
    private string RequestHash(Guid ticket,object body)=>Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(jwt.Value.SigningKey),
        Encoding.UTF8.GetBytes("QrSupportRequest:"+JsonSerializer.Serialize(new{Ticket=ticket,Body=body},GateStore.Json))));
    public Task<GateQrReceipt> ChallengeAsync(GateQrActor actor,string? code,Guid ticketId,GateQrChallengeRequest r,string key,CancellationToken ct)
    {
        GateQrRules.Validate(r,key);
        return Write(actor,code,ticketId,r.RequestId,r.OperationId,r,"CHALLENGE",async s=>
        {
            var t=await Ticket(s,ticketId,ct);Identity(t,r.FullName,r.BirthDate);
            if(!t.EmailConfirmed || string.IsNullOrWhiteSpace(t.Email))throw new GateException("QR_ACCOUNT_EMAIL_REQUIRED",409);
            await Intent(s,actor,t,r.OperationId,r.Action,r.Reason,ct);
            var challenge=Guid.NewGuid();var otp=RandomNumberGenerator.GetInt32(0,1_000_000).ToString("D6");var expires=s.Now.AddMinutes(5);
            await s.Execute("""
                INSERT INTO metro.email_verification_challenges(id,user_id,purpose,code_hash,created_at,expires_at)
                VALUES(@id,@holder,'TICKET_QR_SUPPORT',@hash,@now,@expires);
                INSERT INTO metro.gate_qr_challenges(operator_id,challenge_id,intent_id) VALUES(@op,@id,@operation);
                INSERT INTO metro.gate_email_outbox(id,operator_id,challenge_id,kind,protected_payload,created_at,next_attempt_at,expires_at)
                VALUES(@id,@op,@id,'OTP',@payload,@now,@now,@expires)
                """,ct,("id",challenge),("holder",t.Holder),("hash",CodeHash(challenge,otp)),("expires",expires),("operation",r.OperationId),
                ("payload",protection.Protect(new(challenge,t.Email,"Mã xác minh hỗ trợ vé Metro",$"Mã xác minh hỗ trợ QR của bạn: {otp}. Mã có hiệu lực 5 phút, chỉ dùng cho yêu cầu đang chờ. Không cung cấp mã cho người khác ngoài phiên hỗ trợ đã xác minh."))));
            await Audit(s,t.Id,r.RequestId,"REQUEST_QR_SUPPORT_EMAIL",r.Reason,ct);
            return new(r.RequestId,r.OperationId,t.Id,"QUEUED","QR_EMAIL_QUEUED",challenge,expires,DeliveryStatus:"QUEUED");
        },ct);
    }
    public Task<GateQrReceipt> AccessAsync(GateQrActor actor,string? code,Guid ticketId,GateQrAccessRequest r,string key,CancellationToken ct)
    {
        GateQrRules.Validate(r,key);
        return Write(actor,code,ticketId,r.RequestId,r.OperationId,r,"ACCESS",async s=>
        {
            var t=await Ticket(s,ticketId,ct);Identity(t,r.FullName,r.BirthDate);
            await Intent(s,actor,t,r.OperationId,r.Action,r.Reason,ct);
            DateTimeOffset expires;
            if(r.VerificationMethod=="EMAIL")
            {
                var proof=(await s.Rows<Proof>("""
                    SELECT c.id AS "Id",c.code_hash AS "Hash",c.expires_at AS "Expires",c.failed_attempts AS "Attempts",
                     c.consumed_at AS "Consumed",c.superseded_at AS "Superseded"
                    FROM metro.email_verification_challenges c JOIN metro.gate_qr_challenges q ON q.challenge_id=c.id
                    WHERE q.operator_id=@op AND q.intent_id=@operation AND c.id=@challenge AND c.user_id=@holder AND c.purpose='TICKET_QR_SUPPORT' FOR UPDATE OF c
                    """,ct,("operation",r.OperationId),("challenge",r.ChallengeId),("holder",t.Holder))).SingleOrDefault();
                if(proof is null || proof.Consumed is not null || proof.Superseded is not null || proof.Attempts>=5 || proof.Expires<=s.Now)
                    return new(r.RequestId,r.OperationId,t.Id,"DENIED","INVALID_QR_EMAIL_CODE");
                if(!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(proof.Hash),Encoding.ASCII.GetBytes(CodeHash(proof.Id,r.EmailCode!))))
                {
                    await s.Execute("UPDATE metro.email_verification_challenges SET failed_attempts=failed_attempts+1,superseded_at=CASE WHEN failed_attempts+1>=5 THEN @now ELSE superseded_at END WHERE id=@challenge",ct,("challenge",proof.Id));
                    return new(r.RequestId,r.OperationId,t.Id,"DENIED","INVALID_QR_EMAIL_CODE");
                }
                expires=proof.Expires;
                if(await s.Scalar("""
                    UPDATE metro.email_verification_challenges SET consumed_at=clock_timestamp() WHERE id=@challenge
                      AND consumed_at IS NULL AND superseded_at IS NULL AND failed_attempts<5 AND expires_at>clock_timestamp() RETURNING true
                    """,ct,("challenge",proof.Id)) is not true)
                    return new(r.RequestId,r.OperationId,t.Id,"DENIED","INVALID_QR_EMAIL_CODE");
            }
            else expires=new DateTimeOffset((DateTime)(await s.Scalar("SELECT expires_at FROM metro.auth_sessions WHERE id=@session",ct,("session",actor.SessionId)))!);
            var credential=t.Credential;var version=t.Version;
            if(r.Action=="REPLACE")
            {
                credential=Guid.NewGuid();version=(int)(await s.Scalar("SELECT COALESCE(max(version),0)+1 FROM metro.ticket_credentials WHERE operator_id=@op AND ticket_id=@ticket AND credential_type='QR'",ct,("ticket",t.Id)))!;
                var token=qr.Token(s.Operator,t.Reference,credential,version,qr.ActiveKeyId);_=TicketQrCodec.Png(token);
                await s.Execute("""
                    UPDATE metro.ticket_credentials SET status='REPLACED',revoked_at=@now WHERE operator_id=@op AND id=@old;
                    INSERT INTO metro.ticket_credentials(id,operator_id,ticket_id,credential_type,token_hash,status,version,key_id,issued_at)
                    VALUES(@new,@op,@ticket,'QR',@hash,'ACTIVE',@version,@key,@now);
                    UPDATE metro.ticket_credentials SET replaced_by_id=@new WHERE operator_id=@op AND id=@old
                    """,ct,("old",t.Credential),("new",credential),("ticket",t.Id),("hash",TicketQrCodec.Hash(token)),("version",version),("key",qr.ActiveKeyId));
                await NotifyReplacement(s,t,credential,ct);
            }
            else
            {
                var token=qr.Token(s.Operator,t.Reference,t.Credential,t.Version,t.KeyId);
                if(!TicketQrCodec.Matches(token,t.Hash))throw new GateException("QR_INTEGRITY_ERROR",503);
                _=TicketQrCodec.Png(token);
            }
            // A completed action no longer needs its queued OTP. Replacement also invalidates pending
            // actions bound to the credential just retired; do not send their now-useless codes.
            await s.Execute("""
                UPDATE metro.email_verification_challenges c SET superseded_at=@now FROM metro.gate_qr_challenges q
                 JOIN metro.gate_qr_intents i ON i.operator_id=q.operator_id AND i.id=q.intent_id
                WHERE q.challenge_id=c.id AND q.operator_id=@op AND c.consumed_at IS NULL AND c.superseded_at IS NULL
                  AND (i.id=@operation OR (@action='REPLACE' AND i.ticket_id=@ticket AND i.credential_id=@old))
                """,ct,("operation",r.OperationId),("action",r.Action),("ticket",t.Id),("old",t.Credential));
            await s.Execute("""
                INSERT INTO metro.gate_qr_verifications(operator_id,request_id,ticket_id,method,challenge_id,evidence_reference,reason,before_credential_id,after_credential_id)
                VALUES(@op,@request,@ticket,@method,@challenge::uuid,@evidence::text,@reason,@old,@new)
                """,ct,("request",r.RequestId),("ticket",t.Id),("method",r.VerificationMethod),("challenge",r.ChallengeId),
                ("evidence",r.EvidenceReference),("reason",r.Reason),("old",t.Credential),("new",credential));
            await Audit(s,t.Id,r.RequestId,r.Action=="REPLACE"?"REPLACE_TICKET_QR":"DISPLAY_TICKET_QR",r.Reason,ct);
            return new(r.RequestId,r.OperationId,t.Id,"READY",r.Action=="REPLACE"?"QR_REPLACED":"QR_VERIFIED",ExpiresAt:expires,CredentialId:credential,Version:version);
        },ct);
    }
    private static Task Audit(GateStore s,Guid ticket,Guid request,string action,string reason,CancellationToken ct)=>s.Execute("""
        INSERT INTO metro.audit_logs(operator_id,actor_user_id,action,entity_type,entity_id,changed_fields,reason,correlation_id)
        VALUES(@op,@actor,@action,'TICKET',@ticket::text,ARRAY['qr_support'],@reason,@request)
        """,ct,("ticket",ticket),("request",request),("action",action),("reason",reason));
    private sealed record Saved(string Actor,Guid Session,string Hash,string Response);
    private sealed record Proof(Guid Id,string Hash,DateTimeOffset Expires,short Attempts,DateTimeOffset? Consumed,DateTimeOffset? Superseded);
    private sealed record SupportTicket(Guid Id,string Code,Guid Reference,string Holder,string Status,DateTimeOffset? Start,DateTimeOffset? End,
        string Email,bool EmailConfirmed,string? Stamp,string Name,DateOnly Birth,Guid Credential,int Version,string KeyId,string Hash,bool InJourney);
}
