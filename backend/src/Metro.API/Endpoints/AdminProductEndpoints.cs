using System.Security.Claims;
using Metro.API.Filters;
using Metro.Application.Abstractions.Fare;

namespace Metro.API.Endpoints;

public static class AdminProductEndpoints
{
    public static IEndpointRouteBuilder MapAdminProductEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin/products")
            .WithTags("Admin — Products")
            .RequireAuthorization("Admin");

        group.MapGet("", (string? status, ClaimsPrincipal principal, IAdminProductService service, CancellationToken ct) =>
                service.ListAsync(status, UserId(principal), ct))
            .WithName("ListAdminProducts")
            .WithSummary("UC25 — Danh sách loại vé của đơn vị vận hành")
            .Produces<IReadOnlyList<AdminTicketProductResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("", async (CreateTicketProductRequest request, ClaimsPrincipal principal, IAdminProductService service, CancellationToken ct) =>
            {
                var created = await service.CreateAsync(request, UserId(principal), ct);
                return Results.Created($"/api/v1/admin/products/{created.Id}", created);
            })
            .AddEndpointFilter<IdempotencyEndpointFilter>()
            .WithName("CreateAdminProduct")
            .WithSummary("UC25 — Tạo loại vé mới (chặn thiếu duration, chặn vé 3 ngày, ghi audit, hỗ trợ Idempotency-Key)")
            .Produces<AdminTicketProductResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPut("/{id:guid}", async (Guid id, UpdateTicketProductRequest request, ClaimsPrincipal principal, IAdminProductService service, CancellationToken ct) =>
                Results.Ok(await service.UpdateAsync(id, request, UserId(principal), ct)))
            .AddEndpointFilter<IdempotencyEndpointFilter>()
            .WithName("UpdateAdminProduct")
            .WithSummary("UC25 — Cập nhật thông tin/trạng thái loại vé (không xóa vật lý, ghi audit, hỗ trợ Idempotency-Key)")
            .Produces<AdminTicketProductResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    private static string UserId(ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? principal.FindFirstValue("sub")
        ?? throw new FareException("UNAUTHENTICATED", StatusCodes.Status401Unauthorized, "Missing subject claim.");
}
