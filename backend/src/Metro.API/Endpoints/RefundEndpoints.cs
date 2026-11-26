using System.Security.Claims;
using Metro.Application.Abstractions.Payment;
using Microsoft.AspNetCore.Mvc;

namespace Metro.API.Endpoints;

public static class RefundEndpoints
{
    public static IEndpointRouteBuilder MapRefundEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/refunds").WithTags("Hủy/hoàn").RequireAuthorization();
        group.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store, private";
            return await next(context);
        });

        group.MapPost("", async (HttpContext http, CreateRefundRequest request, string? operatorCode,
            [FromHeader(Name = "Idempotency-Key")] string key, IRefundService service, CancellationToken ct) =>
        {
            var result = await service.CreateAsync(request, Actor(http), operatorCode, Key(http, key), ct);
            return TypedResults.Created($"/api/v1/refunds/{result.Id}", result);
        }).WithName("CreateRefund").WithSummary("Request refund for an eligible payment/ticket (UC42 / BE-16)")
            .Produces<RefundResponse>(201).ProducesProblem(400).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);

        group.MapPost("/{id:guid}/approve", async (HttpContext http, Guid id, ApproveRefundRequest request, string? operatorCode,
            [FromHeader(Name = "Idempotency-Key")] string key, IRefundService service, CancellationToken ct) =>
        {
            var result = await service.ApproveAsync(id, request, Actor(http), operatorCode, Key(http, key), ct);
            return TypedResults.Ok(result);
        }).RequireAuthorization("Admin")
            .WithName("ApproveRefund").WithSummary("Approve and process refund (UC42 / BE-16) - Admin only")
            .Produces<RefundResponse>(200).ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);

        group.MapGet("/{id:guid}", async (HttpContext http, Guid id, string? operatorCode, IRefundService service, CancellationToken ct) =>
        {
            var result = await service.GetAsync(id, Actor(http), operatorCode, ct);
            return TypedResults.Ok(result);
        }).WithName("GetRefund").WithSummary("Get refund request detail")
            .Produces<RefundResponse>(200).ProducesProblem(403).ProducesProblem(404);

        return app;
    }

    private static string Actor(HttpContext http) =>
        http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? http.User.FindFirstValue("sub")
        ?? throw new RefundException("AUTH_REQUIRED", 401, "Authentication required.");

    private static string Key(HttpContext http, string key) =>
        http.Request.Headers["Idempotency-Key"].Count == 1
            ? key : throw new RefundException("IDEMPOTENCY_KEY_REQUIRED", 400, "Idempotency-Key header is required.");
}
