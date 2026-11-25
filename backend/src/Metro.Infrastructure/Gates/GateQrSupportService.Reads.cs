using System.Text.Json;
using Metro.Application.Abstractions.Gates;
using Metro.Application.Ticketing;
using Metro.Infrastructure.Ticketing;

namespace Metro.Infrastructure.Gates;

public sealed partial class GateQrSupportService
{
    public Task<GateQrReceipt> GetAsync(GateQrActor actor,string? code,Guid request,CancellationToken ct)=>Run(actor,code,request,async s=>
    {
        var saved=await Receipt(s,actor,request,ct)??throw new GateException("QR_SUPPORT_REQUEST_NOT_FOUND",404);
        var receipt=JsonSerializer.Deserialize<GateQrReceipt>(saved.Response,GateStore.Json)!;
        if(receipt.ChallengeId is { } challenge)
        {
            var delivery=await s.Scalar("SELECT status FROM metro.gate_email_outbox WHERE operator_id=@op AND challenge_id=@id",ct,("id",challenge)) as string;
            receipt=receipt with{DeliveryStatus=delivery};
        }
        return receipt;
    },ct);

    public Task<byte[]> ImageAsync(GateQrActor actor,string? code,Guid request,CancellationToken ct)=>Run(actor,code,request,async s=>
    {
        var saved=await Receipt(s,actor,request,ct)??throw new GateException("QR_SUPPORT_REQUEST_NOT_FOUND",404);
        var receipt=JsonSerializer.Deserialize<GateQrReceipt>(saved.Response,GateStore.Json)!;
        if(receipt.Status!="READY" || receipt.ExpiresAt is null || receipt.ExpiresAt<=s.Now)throw new GateException("QR_VERIFICATION_REQUIRED",403);
        var t=await Ticket(s,receipt.TicketId,ct);
        if(receipt.CredentialId!=t.Credential || receipt.Version!=t.Version ||
            await s.Scalar("SELECT EXISTS(SELECT 1 FROM metro.gate_qr_intents WHERE operator_id=@op AND id=@id AND profile_hash=@hash)",ct,
                ("id",receipt.OperationId),("hash",IdentityHash(t))) is not true)
            throw new GateException("QR_SUPPORT_CONTEXT_CHANGED",409);
        var token=qr.Token(s.Operator,t.Reference,t.Credential,t.Version,t.KeyId);
        if(!TicketQrCodec.Matches(token,t.Hash))throw new GateException("QR_INTEGRITY_ERROR",503);
        var png=TicketQrCodec.Png(token);
        await Audit(s,t.Id,request,"VIEW_VERIFIED_TICKET_QR","Verified support session",ct);
        return png;
    },ct);

    private async Task NotifyReplacement(GateStore s,SupportTicket t,Guid credential,CancellationToken ct)
    {
        var day=TicketRules.LocalDate(s.Now);
        var recipients=await s.Rows<Recipient>("""
            SELECT u."Id" AS "Id",u."Email" AS "Email" FROM public."AspNetUsers" u WHERE u."EmailConfirmed" AND
             (u."Id"=@holder OR u."Id" IN(SELECT g.guardian_user_id FROM metro.tickets t
              JOIN metro.guardian_links g ON g.operator_id=t.operator_id AND g.id=t.guardian_link_id
              WHERE t.operator_id=@op AND t.id=@ticket AND g.child_user_id=t.holder_user_id AND g.guardian_user_id=t.guardian_user_id
              AND g.status='ACTIVE' AND g.verification_type='GUARDIANSHIP' AND g.verified_at IS NOT NULL
              AND (g.valid_from IS NULL OR g.valid_from<=@day) AND (g.valid_to IS NULL OR g.valid_to>=@day)))
            """,ct,("holder",t.Holder),("ticket",t.Id),("day",day));
        foreach(var recipient in recipients)
        {
            foreach(var channel in new[]{"IN_APP","EMAIL_MOCK"})
            {
                var id=(Guid?)(await s.Scalar("""
                    INSERT INTO metro.notifications(operator_id,recipient_user_id,notification_type,channel,template_code,content_reference,mandatory,correlation_id)
                    VALUES(@op,@recipient,'TICKET_QR_CHANGED',@channel,'BE10_QR_CHANGED',
                      jsonb_build_object('ticketId',@ticket::uuid,'credentialId',@credential::uuid),true,
                      md5(@credential::text||@recipient||@channel||'QR_CHANGED')::uuid)
                    ON CONFLICT(operator_id,correlation_id) DO NOTHING RETURNING id
                    """,ct,("recipient",recipient.Id),("channel",channel),("ticket",t.Id),("credential",credential)));
                if(channel!="EMAIL_MOCK" || id is null)continue;
                await s.Execute("""
                    INSERT INTO metro.gate_email_outbox(id,operator_id,notification_id,kind,protected_payload,created_at,next_attempt_at)
                    VALUES(@id,@op,@id,'QR_CHANGED',@payload,@now,@now)
                    """,ct,("id",id),("payload",protection.Protect(new(id.Value,recipient.Email,"Mã QR vé Metro đã được thay",
                        $"Mã QR của vé {t.Code} đã được thay sau xác minh tại ga. Mã cũ không còn hiệu lực. Vé, thời hạn và hành trình được giữ nguyên. Hãy đăng nhập để xem vé; nếu bạn không yêu cầu thay mã, vui lòng liên hệ nhân viên hỗ trợ."))));
            }
        }
    }
    private sealed record Recipient(string Id,string Email);
}
