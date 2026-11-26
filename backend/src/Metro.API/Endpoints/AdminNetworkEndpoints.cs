using System.Security.Claims;
using Metro.API.Filters;
using Metro.Application.Abstractions.Network;

namespace Metro.API.Endpoints;

public static class AdminNetworkEndpoints
{
    public static IEndpointRouteBuilder MapAdminNetworkEndpoints(this IEndpointRouteBuilder app)
    {
        var lines = Group(app, "/api/v1/admin/lines");
        var stations = Group(app, "/api/v1/admin/stations");

        lines.MapGet("", (string? status, ClaimsPrincipal p, IAdminNetworkService s, CancellationToken ct) => s.ListLinesAsync(status, UserId(p), ct))
            .WithName("ListAdminLines").WithSummary("UC24 — Danh sách tuyến và thứ tự ga kèm thời gian chạy")
            .Produces<IReadOnlyList<AdminLineResponse>>(StatusCodes.Status200OK);
        lines.MapGet("/{id:guid}", (Guid id, ClaimsPrincipal p, IAdminNetworkService s, CancellationToken ct) => s.GetLineAsync(id, UserId(p), ct))
            .WithName("GetAdminLine").WithSummary("UC24 — Chi tiết tuyến và cấu hình trạm dừng")
            .Produces<AdminLineResponse>(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status404NotFound);
        lines.MapPost("", async (CreateLineRequest req, ClaimsPrincipal p, IAdminNetworkService s, CancellationToken ct) =>
            { var c = await s.CreateLineAsync(req, UserId(p), ct); return Results.Created($"/api/v1/admin/lines/{c.Id}", c); })
            .AddEndpointFilter<IdempotencyEndpointFilter>().WithName("CreateAdminLine").WithSummary("UC24 — Tạo tuyến mới (ghi audit, Idempotency-Key)")
            .Produces<AdminLineResponse>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status409Conflict);
        lines.MapPut("/{id:guid}", async (Guid id, UpdateLineRequest req, ClaimsPrincipal p, IAdminNetworkService s, CancellationToken ct) =>
                Results.Ok(await s.UpdateLineAsync(id, req, UserId(p), ct)))
            .AddEndpointFilter<IdempotencyEndpointFilter>().WithName("UpdateAdminLine").WithSummary("UC24 — Cập nhật tên/trạng thái tuyến (ẩn/ngừng phục vụ)")
            .Produces<AdminLineResponse>(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status404NotFound);
        lines.MapDelete("/{id:guid}", async (Guid id, string? reason, ClaimsPrincipal p, IAdminNetworkService s, CancellationToken ct) =>
            { await s.DeleteLineAsync(id, reason, UserId(p), ct); return Results.NoContent(); })
            .AddEndpointFilter<IdempotencyEndpointFilter>().WithName("DeleteAdminLine").WithSummary("UC24/BR-018 — Xóa tuyến PLANNED chưa tham chiếu; 409 nếu đang tham chiếu")
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        lines.MapPut("/{id:guid}/stations", async (Guid id, SetLineStationsRequest req, ClaimsPrincipal p, IAdminNetworkService s, CancellationToken ct) =>
                Results.Ok(await s.SetLineStationsAsync(id, req, UserId(p), ct)))
            .AddEndpointFilter<IdempotencyEndpointFilter>().WithName("SetAdminLineStations").WithSummary("UC24/BR-050 — Cấu hình thứ tự ga và travel_time_seconds theo chiều")
            .Produces<AdminLineResponse>(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

        stations.MapGet("", (string? status, string? search, ClaimsPrincipal p, IAdminNetworkService s, CancellationToken ct) => s.ListStationsAsync(status, search, UserId(p), ct))
            .WithName("ListAdminStations").WithSummary("UC24 — Danh sách ga và min_transfer_seconds")
            .Produces<IReadOnlyList<AdminStationResponse>>(StatusCodes.Status200OK);
        stations.MapGet("/{id:guid}", (Guid id, ClaimsPrincipal p, IAdminNetworkService s, CancellationToken ct) => s.GetStationAsync(id, UserId(p), ct))
            .WithName("GetAdminStation").WithSummary("UC24 — Chi tiết ga")
            .Produces<AdminStationResponse>(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status404NotFound);
        stations.MapPost("", async (CreateStationRequest req, ClaimsPrincipal p, IAdminNetworkService s, CancellationToken ct) =>
            { var c = await s.CreateStationAsync(req, UserId(p), ct); return Results.Created($"/api/v1/admin/stations/{c.Id}", c); })
            .AddEndpointFilter<IdempotencyEndpointFilter>().WithName("CreateAdminStation").WithSummary("UC24 — Tạo ga mới kèm min_transfer_seconds")
            .Produces<AdminStationResponse>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status409Conflict);
        stations.MapPut("/{id:guid}", async (Guid id, UpdateStationRequest req, ClaimsPrincipal p, IAdminNetworkService s, CancellationToken ct) =>
                Results.Ok(await s.UpdateStationAsync(id, req, UserId(p), ct)))
            .AddEndpointFilter<IdempotencyEndpointFilter>().WithName("UpdateAdminStation").WithSummary("UC24 — Cập nhật ga, min_transfer_seconds và trạng thái")
            .Produces<AdminStationResponse>(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status404NotFound);
        stations.MapDelete("/{id:guid}", async (Guid id, string? reason, ClaimsPrincipal p, IAdminNetworkService s, CancellationToken ct) =>
            { await s.DeleteStationAsync(id, reason, UserId(p), ct); return Results.NoContent(); })
            .AddEndpointFilter<IdempotencyEndpointFilter>().WithName("DeleteAdminStation").WithSummary("UC24/BR-018 — Xóa ga PLANNED chưa tham chiếu; 409 nếu đang tham chiếu")
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    private static RouteGroupBuilder Group(IEndpointRouteBuilder app, string prefix)
    {
        var g = app.MapGroup(prefix).WithTags("Admin — Network (BE-13)").RequireAuthorization("Admin");
        g.AddEndpointFilter(async (ctx, next) => { ctx.HttpContext.Response.Headers.CacheControl = "no-store"; return await next(ctx); });
        return g;
    }

    private static string UserId(ClaimsPrincipal p) =>
        p.FindFirstValue(ClaimTypes.NameIdentifier) ?? p.FindFirstValue("sub")
        ?? throw new NetworkAdminException("UNAUTHENTICATED", StatusCodes.Status401Unauthorized, "Missing subject claim.");
}
