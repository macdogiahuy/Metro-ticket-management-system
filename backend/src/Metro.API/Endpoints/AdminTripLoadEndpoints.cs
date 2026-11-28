using System.Security.Claims;
using Metro.API.Filters;
using Metro.Application.Abstractions.Network;

namespace Metro.API.Endpoints;

public static class AdminTripLoadEndpoints
{
    public static IEndpointRouteBuilder MapAdminTripLoadEndpoints(this IEndpointRouteBuilder app)
    {
        var load = app.MapGroup("/api/v1/admin/trip-load").WithTags("Admin — Trip Load & ETA (BE-13)").RequireAuthorization("Admin");
        load.AddEndpointFilter(async (ctx, next) => { ctx.HttpContext.Response.Headers.CacheControl = "no-store"; return await next(ctx); });

        load.MapGet("/trainsets", (ClaimsPrincipal p, IAdminTripLoadService s, CancellationToken ct) => s.ListTrainsetsAsync(UserId(p), ct))
            .WithName("ListAdminTrainsets").WithSummary("UC29/UC37 — Danh sách đoàn tàu và giới hạn tải max_load_kg từng toa")
            .Produces<IReadOnlyList<AdminTrainsetResponse>>(StatusCodes.Status200OK);
        load.MapPost("/trainsets", async (CreateTrainsetRequest req, ClaimsPrincipal p, IAdminTripLoadService s, CancellationToken ct) =>
            { var c = await s.CreateTrainsetAsync(req, UserId(p), ct); return Results.Created($"/api/v1/admin/trip-load/trainsets/{c.Id}", c); })
            .AddEndpointFilter<IdempotencyEndpointFilter>().WithName("CreateAdminTrainset").WithSummary("UC29/UC37 — Tạo đoàn tàu và các toa kèm max_load_kg")
            .Produces<AdminTrainsetResponse>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status409Conflict);
        load.MapPut("/trainsets/{id:guid}", async (Guid id, UpdateTrainsetRequest req, ClaimsPrincipal p, IAdminTripLoadService s, CancellationToken ct) =>
                Results.Ok(await s.UpdateTrainsetAsync(id, req, UserId(p), ct)))
            .AddEndpointFilter<IdempotencyEndpointFilter>().WithName("UpdateAdminTrainset").WithSummary("UC29/BR-056 — Cập nhật đoàn tàu/toa (không sửa hồi tố chuyến đang chạy)")
            .Produces<AdminTrainsetResponse>(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status404NotFound);

        load.MapGet("/profiles", (Guid? trainsetId, string? status, ClaimsPrincipal p, IAdminTripLoadService s, CancellationToken ct) => s.ListLoadProfilesAsync(trainsetId, status, UserId(p), ct))
            .WithName("ListAdminLoadProfiles").WithSummary("UC37 — Danh sách hồ sơ giới hạn tải theo phiên bản (max_load_kg)")
            .Produces<IReadOnlyList<AdminLoadProfileResponse>>(StatusCodes.Status200OK);
        load.MapPost("/profiles", async (CreateLoadProfileRequest req, ClaimsPrincipal p, IAdminTripLoadService s, CancellationToken ct) =>
            { var c = await s.CreateLoadProfileAsync(req, UserId(p), ct); return Results.Created($"/api/v1/admin/trip-load/profiles/{c.Id}", c); })
            .AddEndpointFilter<IdempotencyEndpointFilter>().WithName("CreateAdminLoadProfile").WithSummary("UC37 — Tạo hồ sơ tải DRAFT (max_load_kg, kiểm tra tổng tải toa)")
            .Produces<AdminLoadProfileResponse>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status409Conflict);
        load.MapPost("/profiles/{id:guid}/approve", async (Guid id, LoadProfileDecisionRequest req, ClaimsPrincipal p, IAdminTripLoadService s, CancellationToken ct) =>
                Results.Ok(await s.ApproveLoadProfileAsync(id, req, UserId(p), ct)))
            .AddEndpointFilter<IdempotencyEndpointFilter>().WithName("ApproveAdminLoadProfile").WithSummary("UC37 — Phê duyệt hồ sơ tải (maker-checker)")
            .Produces<AdminLoadProfileResponse>(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status409Conflict);
        load.MapPost("/profiles/{id:guid}/publish", async (Guid id, LoadProfileDecisionRequest req, ClaimsPrincipal p, IAdminTripLoadService s, CancellationToken ct) =>
                Results.Ok(await s.PublishLoadProfileAsync(id, req, UserId(p), ct)))
            .AddEndpointFilter<IdempotencyEndpointFilter>().WithName("PublishAdminLoadProfile").WithSummary("UC37 — Công bố hồ sơ tải")
            .Produces<AdminLoadProfileResponse>(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status409Conflict);

        load.MapGet("", (Guid? tripId, Guid? lineId, DateOnly? serviceDate, ClaimsPrincipal p, IAdminTripLoadService s, CancellationToken ct) => s.GetTripLoadStatusesAsync(tripId, lineId, serviceDate, UserId(p), ct))
            .WithName("GetAdminTripLoadStatuses").WithSummary("UC38/AC-X18 — Tra cứu trạng thái tải hiện tại (v_trip_load_status)")
            .Produces<IReadOnlyList<AdminTripLoadStatusResponse>>(StatusCodes.Status200OK);
        load.MapPost("/observations", async (RecordTripLoadObservationRequest req, ClaimsPrincipal p, IAdminTripLoadService s, CancellationToken ct) =>
            { var c = await s.RecordLoadObservationAsync(req, UserId(p), ct); return Results.Created($"/api/v1/admin/trip-load/observations/{c.Id}", c); })
            .AddEndpointFilter<IdempotencyEndpointFilter>().WithName("RecordAdminTripLoadObservation").WithSummary("UC38/BR-054 — Nhập tải kg thủ công (1 chữ số thập phân, lưu lịch sử)")
            .Produces<AdminTripLoadObservationResponse>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status409Conflict);
        load.MapPost("/eta", async (RecordTripEtaUpdateRequest req, ClaimsPrincipal p, IAdminTripLoadService s, CancellationToken ct) =>
            { var c = await s.RecordEtaUpdateAsync(req, UserId(p), ct); return Results.Created($"/api/v1/admin/trip-load/eta/{c.Id}", c); })
            .AddEndpointFilter<IdempotencyEndpointFilter>().WithName("RecordAdminTripEtaUpdate").WithSummary("UC29/BR-052 — Nhập ETA thủ công theo chuyến và ga (đồng bộ BE-05)")
            .Produces<AdminTripEtaUpdateResponse>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status409Conflict);
        return app;
    }

    private static string UserId(ClaimsPrincipal p) =>
        p.FindFirstValue(ClaimTypes.NameIdentifier) ?? p.FindFirstValue("sub")
        ?? throw new NetworkAdminException("UNAUTHENTICATED", StatusCodes.Status401Unauthorized, "Missing subject claim.");
}
