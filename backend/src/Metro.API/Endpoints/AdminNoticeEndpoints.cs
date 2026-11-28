using System.Security.Claims;
using Metro.API.Filters;
using Metro.Application.Abstractions.Network;

namespace Metro.API.Endpoints;

public static class AdminNoticeEndpoints
{
    public static IEndpointRouteBuilder MapAdminNoticeEndpoints(this IEndpointRouteBuilder app)
    {
        var notices = app.MapGroup("/api/v1/admin/notices").WithTags("Admin — Service Notices (BE-13)").RequireAuthorization("Admin");
        notices.AddEndpointFilter(async (ctx, next) => { ctx.HttpContext.Response.Headers.CacheControl = "no-store"; return await next(ctx); });

        notices.MapGet("", (string? status, string? audience, bool? activeOnly, ClaimsPrincipal p, IAdminNoticeService s, CancellationToken ct) =>
                s.ListAsync(status, audience, activeOnly ?? false, UserId(p), ct))
            .WithName("ListAdminNotices").WithSummary("UC31 — Danh sách thông báo dịch vụ theo trạng thái/hiệu lực")
            .Produces<IReadOnlyList<AdminServiceNoticeResponse>>(StatusCodes.Status200OK);
        notices.MapGet("/{id:guid}", (Guid id, ClaimsPrincipal p, IAdminNoticeService s, CancellationToken ct) => s.GetAsync(id, UserId(p), ct))
            .WithName("GetAdminNotice").WithSummary("UC31 — Chi tiết thông báo dịch vụ")
            .Produces<AdminServiceNoticeResponse>(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status404NotFound);
        notices.MapPost("", async (CreateServiceNoticeRequest req, ClaimsPrincipal p, IAdminNoticeService s, CancellationToken ct) =>
            { var c = await s.CreateAsync(req, UserId(p), ct); return Results.Created($"/api/v1/admin/notices/{c.Id}", c); })
            .AddEndpointFilter<IdempotencyEndpointFilter>().WithName("CreateAdminNotice").WithSummary("UC31 — Tạo nháp thông báo dịch vụ có cửa sổ hiệu lực [validFrom, validTo)")
            .Produces<AdminServiceNoticeResponse>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status409Conflict);
        notices.MapPut("/{id:guid}", async (Guid id, UpdateServiceNoticeRequest req, ClaimsPrincipal p, IAdminNoticeService s, CancellationToken ct) =>
                Results.Ok(await s.UpdateAsync(id, req, UserId(p), ct)))
            .AddEndpointFilter<IdempotencyEndpointFilter>().WithName("UpdateAdminNotice").WithSummary("UC31 — Cập nhật thông báo DRAFT trước khi công bố")
            .Produces<AdminServiceNoticeResponse>(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status409Conflict);
        notices.MapPost("/{id:guid}/approve", async (Guid id, ServiceNoticeDecisionRequest req, ClaimsPrincipal p, IAdminNoticeService s, CancellationToken ct) =>
                Results.Ok(await s.ApproveAsync(id, req, UserId(p), ct)))
            .AddEndpointFilter<IdempotencyEndpointFilter>().WithName("ApproveAdminNotice").WithSummary("UC31 — Phê duyệt thông báo dịch vụ (maker-checker trừ emergencyTemplate)")
            .Produces<AdminServiceNoticeResponse>(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status409Conflict);
        notices.MapPost("/{id:guid}/publish", async (Guid id, ServiceNoticeDecisionRequest req, ClaimsPrincipal p, IAdminNoticeService s, CancellationToken ct) =>
                Results.Ok(await s.PublishAsync(id, req, UserId(p), ct)))
            .AddEndpointFilter<IdempotencyEndpointFilter>().WithName("PublishAdminNotice").WithSummary("UC31 — Công bố thông báo dịch vụ (từ chối 422 nếu đã hết hạn, đồng bộ BE-05)")
            .Produces<AdminServiceNoticeResponse>(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        notices.MapPost("/{id:guid}/cancel", async (Guid id, ServiceNoticeDecisionRequest req, ClaimsPrincipal p, IAdminNoticeService s, CancellationToken ct) =>
                Results.Ok(await s.CancelOrExpireAsync(id, req, UserId(p), ct)))
            .AddEndpointFilter<IdempotencyEndpointFilter>().WithName("CancelAdminNotice").WithSummary("UC31 — Hủy hoặc kết thúc hiệu lực thông báo dịch vụ (đồng bộ BE-05)")
            .Produces<AdminServiceNoticeResponse>(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status409Conflict);
        return app;
    }

    private static string UserId(ClaimsPrincipal p) =>
        p.FindFirstValue(ClaimTypes.NameIdentifier) ?? p.FindFirstValue("sub")
        ?? throw new NetworkAdminException("UNAUTHENTICATED", StatusCodes.Status401Unauthorized, "Missing subject claim.");
}
