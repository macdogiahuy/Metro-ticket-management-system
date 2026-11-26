using System.Security.Claims;
using Metro.Application.Abstractions.Gates;
using Microsoft.AspNetCore.Mvc;

namespace Metro.API.Endpoints;

public static class GateEndpoints
{
    public static IEndpointRouteBuilder MapGateEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/gate").WithTags("Gate validation (BE-10)")
            .RequireAuthorization(p => p.RequireRole("StationStaff", "Admin"));
        group.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store, private";
            return await next(context);
        });
        group.MapPost("/scans", async (HttpContext http, GateScanRequest request, string? operatorCode,
            [FromHeader(Name = "Idempotency-Key")] string key, IGateService gate, CancellationToken ct) =>
            Response(await gate.ScanAsync(Actor(http), operatorCode, request, Key(http, key), ct)))
            .WithName("ScanGateTicket").WithSummary("UC16 — Scan one QR or an atomic guardian/child pair; no offline validation")
            .Produces<GateResult>().Produces<GateResult>(202).ProducesProblem(400).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409).ProducesProblem(503);
        group.MapGet("/scans/{requestId:guid}", async (HttpContext http, Guid requestId, string? operatorCode, IGateService gate, CancellationToken ct) =>
            Response(await gate.ResultAsync(Actor(http), operatorCode, requestId, ct)))
            .WithName("GetGateScanResult").WithSummary("Recover an acknowledged result before submitting a new scan")
            .Produces<GateResult>().Produces<GateResult>(202).ProducesProblem(403);
        group.MapPost("/resolutions", async (HttpContext http, GateResolutionRequest request, string? operatorCode,
            [FromHeader(Name = "Idempotency-Key")] string key, IGateService gate, CancellationToken ct) =>
            Response(await gate.ResolveAsync(Actor(http), operatorCode, request, Key(http, key), ct)))
            .WithName("ResolveGateException").WithSummary("UC17 — Evidence-based exit, missing entry, correction or escalation; preserve original events")
            .Produces<GateResult>().Produces<GateResult>(202).ProducesProblem(400).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);
        group.MapPost("/lookup", (HttpContext http, GateLookupRequest request, string? operatorCode, IGateService gate, CancellationToken ct) =>
            gate.LookupAsync(Actor(http), operatorCode, request, ct))
            .WithName("LookupGateTicket").WithSummary("UC18 — Audited minimal ticket lookup; does not reveal or reissue QR")
            .Produces<GateTicketLookup>().ProducesProblem(400).ProducesProblem(403).ProducesProblem(404);
        group.MapGet("/escalations",(HttpContext http,string? operatorCode,int? page,string? status,IGateService gate,CancellationToken ct)=>
            gate.EscalationsAsync(Actor(http),operatorCode,page??1,status??"OPEN",ct))
            .RequireAuthorization("Admin").WithName("ListGateEscalations").WithSummary("UC17 — Admin support queue; closing a support case never grants passage")
            .Produces<GateEscalationPage>().ProducesProblem(400).ProducesProblem(403).ProducesProblem(503);
        group.MapPost("/tickets/{ticketId:guid}/qr-challenges", async (HttpContext http, Guid ticketId, GateQrChallengeRequest request,
            string? operatorCode, [FromHeader(Name="Idempotency-Key")] string key, IGateQrSupportService support, CancellationToken ct) =>
            QrResponse(await support.ChallengeAsync(QrActor(http),operatorCode,ticketId,request,Key(http,key),ct)))
            .WithName("RequestGateQrVerification").WithSummary("UC18 — Queue a purpose/ticket/action-bound email code; never disclose the code to staff")
            .Produces<GateQrReceipt>(202).ProducesProblem(400).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409).ProducesProblem(429).ProducesProblem(503);
        group.MapPost("/tickets/{ticketId:guid}/qr-support", async (HttpContext http, Guid ticketId, GateQrAccessRequest request,
            string? operatorCode, [FromHeader(Name="Idempotency-Key")] string key, IGateQrSupportService support, CancellationToken ct) =>
            QrResponse(await support.AccessAsync(QrActor(http),operatorCode,ticketId,request,Key(http,key),ct)))
            .WithName("VerifyGateQrSupport").WithSummary("UC18 — Verify name/birth date and email code or documented no-email proof; display or atomically replace QR")
            .Produces<GateQrReceipt>().Produces<GateQrReceipt>(202).ProducesProblem(400).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409).ProducesProblem(503);
        group.MapGet("/qr-requests/{requestId:guid}", async (HttpContext http, Guid requestId, string? operatorCode,
            IGateQrSupportService support, CancellationToken ct)=>QrResponse(await support.GetAsync(QrActor(http),operatorCode,requestId,ct)))
            .WithName("GetGateQrSupportResult").WithSummary("Recover a QR-support receipt and current email queue state in the same staff session")
            .Produces<GateQrReceipt>().Produces<GateQrReceipt>(202).ProducesProblem(403).ProducesProblem(404).ProducesProblem(503);
        group.MapGet("/qr-requests/{requestId:guid}/image", async (HttpContext http, Guid requestId, string? operatorCode,
            IGateQrSupportService support, CancellationToken ct)=>
        {
            http.Response.Headers.Pragma="no-cache";
            return Results.File(await support.ImageAsync(QrActor(http),operatorCode,requestId,ct),"image/png");
        }).WithName("GetVerifiedGateQrImage").WithSummary("Read current QR only after successful verification; rechecks actor/session/profile/credential and audits access")
            .Produces(200,contentType:"image/png").ProducesProblem(403).ProducesProblem(404).ProducesProblem(409).ProducesProblem(503);
        return app;
    }
    private static IResult Response(GateResult result) => Results.Json(result, statusCode: result.Decision == "Pending" ? 202 : 200);
    private static string Actor(HttpContext http) => http.User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? http.User.FindFirstValue("sub") ?? throw new GateException("GATE_AUTH_REQUIRED", 401);
    private static GateQrActor QrActor(HttpContext http)=>new(Actor(http),Guid.TryParse(http.User.FindFirstValue("session_id"),out var id)
        ? id : throw new GateException("GATE_SESSION_REQUIRED",401));
    private static IResult QrResponse(GateQrReceipt result)=>Results.Json(result,statusCode:result.Status is "QUEUED" or "Pending"?202:200);
    private static string Key(HttpContext http, string key) => http.Request.Headers["Idempotency-Key"].Count == 1
        ? key : throw new GateException("IDEMPOTENCY_KEY_REQUIRED", 400);
}
