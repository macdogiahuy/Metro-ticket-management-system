using System.Security.Claims;
using Metro.Application.Abstractions.Operations;

namespace Metro.API.Endpoints;

public static class OperationsEndpoints
{
    public static IEndpointRouteBuilder MapOperationsEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/v1/admin").WithTags("Service Operations (BE-14)").RequireAuthorization("Admin");
        admin.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            return await next(context);
        });
        admin.MapPost("/trips/{id:guid}/eta", (Guid id, string operatorCode, UpdateEtaRequest request,
            HttpContext http, IOperationsService service, CancellationToken ct) =>
            service.UpdateEtaAsync(Actor(http), operatorCode, id, request, Key(http), ct))
            .WithName("RecordTripEta").WithSummary("UC30/36 — Append a manual station ETA; requires Idempotency-Key and reason.")
            .Produces<OperationReceipt>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409).ProducesProblem(422);
        admin.MapPost("/trips/{id:guid}/load", (Guid id, string operatorCode, RecordLoadRequest request,
            HttpContext http, IOperationsService service, CancellationToken ct) =>
            service.RecordLoadAsync(Actor(http), operatorCode, id, request, Key(http), ct))
            .WithName("RecordTripLoad").WithSummary("BR-054/055 — Append manual kg (one decimal); never reserves seats.")
            .Produces<OperationReceipt>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409).ProducesProblem(422);
        admin.MapPut("/trips/{id:guid}/status", (Guid id, string operatorCode, UpdateTripStateRequest request,
            HttpContext http, IOperationsService service, CancellationToken ct) =>
            service.UpdateTripStateAsync(Actor(http), operatorCode, id, request, Key(http), ct))
            .WithName("UpdateTripOperationalState").WithSummary("UC30/36 — Update trip status without inventing arrival/departure or changing journeys.")
            .Produces<OperationReceipt>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);
        admin.MapGet("/trips/{id:guid}/operations", (Guid id, string operatorCode, int? page,
            HttpContext http, IOperationsService service, CancellationToken ct) =>
            service.GetHistoryAsync(Actor(http), operatorCode, id, page ?? 1, ct))
            .WithName("GetTripOperationsHistory").WithSummary("Admin-only ETA/load history, 50 rows per collection/page.")
            .Produces<TripOperationsHistory>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404);
        admin.MapPost("/disruptions", (string operatorCode, CreateDisruptionRequest request,
            HttpContext http, IOperationsService service, CancellationToken ct) =>
            service.CreateDisruptionAsync(Actor(http), operatorCode, request, Key(http), ct))
            .WithName("CreateServiceDisruption").WithSummary("UC30 — Create a draft for exactly one line, station or trip; not public yet.")
            .Produces<OperationReceipt>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404);
        admin.MapPost("/disruptions/{id:guid}/publish", (Guid id, string operatorCode, DisruptionDecisionRequest request,
            HttpContext http, IOperationsService service, CancellationToken ct) =>
            service.DecideDisruptionAsync(Actor(http), operatorCode, id, request, false, Key(http), ct))
            .WithName("PublishServiceDisruption").WithSummary("Publish verified service impact, notice and affected-user outbox atomically.")
            .Produces<OperationReceipt>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);
        admin.MapPost("/disruptions/{id:guid}/resolve", (Guid id, string operatorCode, DisruptionDecisionRequest request,
            HttpContext http, IOperationsService service, CancellationToken ct) =>
            service.DecideDisruptionAsync(Actor(http), operatorCode, id, request, true, Key(http), ct))
            .WithName("ResolveServiceDisruption").WithSummary("Record recovery, expire the notice and preserve later manual/terminal trip changes.")
            .Produces<OperationReceipt>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);
        admin.MapGet("/disruptions", (string operatorCode, int? page, HttpContext http,
            IOperationsService service, CancellationToken ct) => service.ListDisruptionsAsync(Actor(http), operatorCode, page ?? 1, ct))
            .WithName("ListServiceDisruptions").WithSummary("Admin-only drafts and service history, 50 rows/page.")
            .Produces<IReadOnlyList<DisruptionResponse>>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404);

        app.MapGet("/api/v1/trips/{id:guid}/load", (Guid id, string operatorCode, IOperationsService service, CancellationToken ct) =>
            service.GetLoadAsync(operatorCode, id, ct)).AllowAnonymous().WithTags("Catalog")
            .WithName("GetTripLoad").WithSummary("Current manual kg and configured limit; missing data denies entry, never a seat guarantee.")
            .Produces<TripLoadResponse>().ProducesProblem(400).ProducesProblem(404);
        return app;
    }

    private static string Actor(HttpContext http) => http.User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? http.User.FindFirstValue("sub") ?? throw new OperationsException("UNAUTHENTICATED", 401);
    private static string Key(HttpContext http) => http.Request.Headers["Idempotency-Key"].ToString();
}
