using Metro.Application.Abstractions.Gates;

namespace Metro.Infrastructure.Gates;

public sealed partial class GateService
{
    private static async Task RequireAdmin(GateStore s,CancellationToken ct)
    {
        if(await s.Scalar("""
            SELECT EXISTS(SELECT 1 FROM public."AspNetUserRoles" u JOIN public."AspNetRoles" r ON r."Id"=u."RoleId"
             WHERE u."UserId"=@actor AND r."Name"='Admin')
            """,ct) is not true)throw new GateException("GATE_ADMIN_REQUIRED",403);
    }
    public async Task<GateEscalationPage> EscalationsAsync(string actor,string? operatorCode,int page,string status,CancellationToken ct)
    {
        if(page is <1 or >100_000 || status is not ("OPEN" or "CLOSED" or "ALL"))throw new GateException("INVALID_GATE_SUPPORT_FILTER",400);
        GateEscalationPage? result=null;
        var read=await Transaction(actor,operatorCode,Guid.NewGuid(),null,async s=>
        {
            await RequireAdmin(s,ct);
            const string cases="""
                SELECT r.request_id AS "RequestId",r.source_request_id AS "SourceRequestId",q.actor_user_id AS "ActorUserId",q.created_at AS "CreatedAt",
                 r.reason AS "Reason",r.evidence_reference AS "EvidenceReference",
                 CASE WHEN EXISTS(SELECT 1 FROM metro.gate_resolutions solved JOIN metro.gate_requests sq
                   ON sq.operator_id=solved.operator_id AND sq.request_id=solved.request_id
                   WHERE solved.operator_id=r.operator_id AND solved.source_request_id=r.source_request_id AND solved.action<>'ESCALATE'
                   AND sq.created_at>=q.created_at) THEN 'CLOSED' ELSE 'OPEN' END AS "Status"
                FROM metro.gate_resolutions r JOIN metro.gate_requests q ON q.operator_id=r.operator_id AND q.request_id=r.request_id
                WHERE r.operator_id=@op AND r.action='ESCALATE'
                """;
            // One statement gives page and total the same snapshot while unrelated scans continue.
            result=(await s.Rows<GateEscalationPage>("WITH cases AS ("+cases+") , filtered AS (SELECT * FROM cases WHERE @status='ALL' OR \"Status\"=@status) "+
                "SELECT COALESCE((SELECT jsonb_agg(row_to_json(p)) FROM (SELECT * FROM filtered ORDER BY \"CreatedAt\" DESC,\"RequestId\" LIMIT 20 OFFSET @offset) p),'[]'::jsonb) AS \"Items\", "+
                "@page AS \"Page\",20 AS \"PageSize\",(SELECT count(*) FROM filtered) AS \"Total\"",
                ct,("status",status),("offset",(page-1)*20),("page",page))).Single();
            await s.Audit(Guid.NewGuid(),"REVIEW_SUPPORT","Administrator reviewed gate support cases",ct);
            return new(Guid.Empty,"Deny","SUPPORT_LIST_ONLY",s.Now,[]);
        },ct);
        return result is null || read.Decision=="Pending"?throw new GateException("GATE_SUPPORT_UNAVAILABLE",503):result;
    }
    private static Task NotifySupport(GateStore s,GateResolutionRequest r,CancellationToken ct)=>s.Execute("""
        INSERT INTO metro.notifications(operator_id,recipient_user_id,notification_type,channel,template_code,content_reference,mandatory,correlation_id)
        SELECT @op,u."Id",'GATE_SUPPORT_REVIEW','IN_APP','BE10_GATE_SUPPORT',jsonb_build_object('requestId',@id::uuid,'sourceRequestId',@source::uuid),true,
         md5(@id::text||u."Id"||'GATE_SUPPORT_REVIEW')::uuid
        FROM public."AspNetUsers" u JOIN metro.operator_accounts m ON m.user_id=u."Id" AND m.operator_id=@op AND m.status='ACTIVE'
        WHERE u."EmailConfirmed" AND EXISTS(SELECT 1 FROM public."AspNetUserRoles" ur JOIN public."AspNetRoles" role ON role."Id"=ur."RoleId"
          WHERE ur."UserId"=u."Id" AND role."Name"='Admin')
        AND NOT EXISTS(SELECT 1 FROM metro.user_profiles p WHERE p.user_id=u."Id" AND p.status IN ('LOCKED','DISABLED','CLOSED'))
        ON CONFLICT(operator_id,correlation_id) DO NOTHING
        """,ct,("id",r.RequestId),("source",r.SourceRequestId));
}
