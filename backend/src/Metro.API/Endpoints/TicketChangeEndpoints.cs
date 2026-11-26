using System.Security.Claims;
using Metro.Application.Abstractions.Ticketing;
using Microsoft.AspNetCore.Mvc;

namespace Metro.API.Endpoints;

public static class TicketChangeEndpoints
{
    public static IEndpointRouteBuilder MapTicketChangeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("Đổi vé").RequireAuthorization();
        group.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store, private";
            return await next(context);
        });

        group.MapPost("/tickets/{id:guid}/change-requests", async (HttpContext http, Guid id, CreateTicketChangeRequest request, string? operatorCode,
            [FromHeader(Name = "Idempotency-Key")] string key, ITicketChangeService service, CancellationToken ct) =>
        {
            var result = await service.CreateAsync(id, request, Actor(http), operatorCode, Key(http, key), ct);
            return TypedResults.Created($"/api/v1/ticket-change-requests/{result.Id}", result);
        }).WithName("CreateTicketChangeRequest").WithSummary("Request ticket exchange due to service disruption (max 1 change/ticket) (UC42 / BE-16)")
            .Produces<TicketChangeResponse>(201).ProducesProblem(400).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);

        group.MapPost("/ticket-change-requests/{id:guid}/approve", async (HttpContext http, Guid id, ApproveTicketChangeRequest request, string? operatorCode,
            [FromHeader(Name = "Idempotency-Key")] string key, ITicketChangeService service, CancellationToken ct) =>
        {
            var result = await service.ApproveAsync(id, request, Actor(http), operatorCode, Key(http, key), ct);
            return TypedResults.Ok(result);
        }).RequireAuthorization("StaffOrAdmin")
            .WithName("ApproveTicketChangeRequest").WithSummary("Approve and apply ticket exchange, revoking old QR (UC42 / BE-16) - Staff or Admin only")
            .Produces<TicketChangeResponse>(200).ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);

        group.MapGet("/ticket-change-requests/{id:guid}", async (HttpContext http, Guid id, string? operatorCode, ITicketChangeService service, CancellationToken ct) =>
        {
            var result = await service.GetAsync(id, Actor(http), operatorCode, ct);
            return TypedResults.Ok(result);
        }).WithName("GetTicketChangeRequest").WithSummary("Get ticket change request detail")
            .Produces<TicketChangeResponse>(200).ProducesProblem(403).ProducesProblem(404);

        return app;
    }

    private static string Actor(HttpContext http) =>
        http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? http.User.FindFirstValue("sub")
        ?? throw new TicketChangeException("AUTH_REQUIRED", 401, "Authentication required.");

    private static string Key(HttpContext http, string key) =>
        http.Request.Headers["Idempotency-Key"].Count == 1
            ? key : throw new TicketChangeException("IDEMPOTENCY_KEY_REQUIRED", 400, "Idempotency-Key header is required.");
}
