using System.Security.Claims;
using Metro.API.Filters;
using Metro.Application.Abstractions.Fare;

namespace Metro.API.Endpoints;

public static class AdminFareEndpoints
{
    public static IEndpointRouteBuilder MapAdminFareEndpoints(this IEndpointRouteBuilder app)
    {
        var fares = app.MapGroup("/api/v1/admin/fares")
            .WithTags("Admin — Fares")
            .RequireAuthorization("Admin");

        fares.MapGet("", (string? status, string? scopeKey, ClaimsPrincipal p, IAdminFareService s, CancellationToken ct) =>
                s.ListVersionsAsync(status, scopeKey, UserId(p), ct))
            .WithName("ListAdminFareVersions")
            .WithSummary("UC25 — Danh sách phiên bản bảng giá")
            .Produces<IReadOnlyList<AdminFarePolicyVersionResponse>>(StatusCodes.Status200OK);

        fares.MapGet("/{id:guid}", (Guid id, ClaimsPrincipal p, IAdminFareService s, CancellationToken ct) =>
                s.GetVersionAsync(id, UserId(p), ct))
            .WithName("GetAdminFareVersion")
            .WithSummary("UC25 — Chi tiết phiên bản bảng giá và các dòng ma trận giá")
            .Produces<AdminFarePolicyVersionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        fares.MapPost("", async (CreateFarePolicyVersionRequest req, ClaimsPrincipal p, IAdminFareService s, CancellationToken ct) =>
            {
                var created = await s.CreateVersionAsync(req, UserId(p), ct);
                return Results.Created($"/api/v1/admin/fares/{created.Id}", created);
            })
            .AddEndpointFilter<IdempotencyEndpointFilter>()
            .WithName("CreateAdminFareVersion")
            .WithSummary("UC25 — Tạo phiên bản bảng giá DRAFT mới (version tự tăng, ghi audit, Idempotency-Key)")
            .Produces<AdminFarePolicyVersionResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status409Conflict);

        fares.MapPost("/{id:guid}/entries", async (Guid id, UpsertFareMatrixEntryRequest req, ClaimsPrincipal p, IAdminFareService s, CancellationToken ct) =>
            {
                var created = await s.AddMatrixEntryAsync(id, req, UserId(p), ct);
                return Results.Created($"/api/v1/admin/fares/{id}/entries/{created.Id}", created);
            })
            .AddEndpointFilter<IdempotencyEndpointFilter>()
            .WithName("AddAdminFareMatrixEntry")
            .WithSummary("UC25 — Thêm dòng giá vào phiên bản DRAFT (khóa NULL-safe, chặn trùng 409, ghi audit)")
            .Produces<AdminFareMatrixEntryResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status409Conflict);

        fares.MapDelete("/{id:guid}/entries/{entryId:guid}", async (Guid id, Guid entryId, string? reason, ClaimsPrincipal p, IAdminFareService s, CancellationToken ct) =>
            {
                await s.DeleteMatrixEntryAsync(id, entryId, reason, UserId(p), ct);
                return Results.NoContent();
            })
            .AddEndpointFilter<IdempotencyEndpointFilter>()
            .WithName("DeleteAdminFareMatrixEntry")
            .WithSummary("UC25 — Xóa dòng giá khỏi phiên bản DRAFT (bị chặn khi đã duyệt/công bố)")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status409Conflict);

        fares.MapPost("/{id:guid}/approve", async (Guid id, FarePolicyDecisionRequest req, ClaimsPrincipal p, IAdminFareService s, CancellationToken ct) =>
                Results.Ok(await s.ApproveVersionAsync(id, req, UserId(p), ct)))
            .AddEndpointFilter<IdempotencyEndpointFilter>()
            .WithName("ApproveAdminFareVersion")
            .WithSummary("UC25 — Phê duyệt phiên bản bảng giá (maker-checker, bắt buộc có ít nhất 1 dòng giá)")
            .Produces<AdminFarePolicyVersionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        fares.MapPost("/{id:guid}/publish", async (Guid id, FarePolicyDecisionRequest req, ClaimsPrincipal p, IAdminFareService s, CancellationToken ct) =>
                Results.Ok(await s.PublishVersionAsync(id, req, UserId(p), ct)))
            .AddEndpointFilter<IdempotencyEndpointFilter>()
            .WithName("PublishAdminFareVersion")
            .WithSummary("UC25 — Công bố phiên bản bảng giá (chặn chồng lấp khoảng hiệu lực 409, ghi audit)")
            .Produces<AdminFarePolicyVersionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    private static string UserId(ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? principal.FindFirstValue("sub")
        ?? throw new FareException("UNAUTHENTICATED", StatusCodes.Status401Unauthorized, "Missing subject claim.");
}
