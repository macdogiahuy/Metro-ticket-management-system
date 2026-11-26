using System.Security.Claims;
using Metro.API.Filters;
using Metro.Application.Abstractions.Network;

namespace Metro.API.Endpoints;

public static class AdminTripEndpoints
{
    public static IEndpointRouteBuilder MapAdminTripEndpoints(this IEndpointRouteBuilder app)
    {
        var trips = app.MapGroup("/api/v1/admin/trips")
            .WithTags("Admin — Trips (BE-13)")
            .RequireAuthorization("Admin");
        trips.AddEndpointFilter(async (ctx, next) =>
        {
            ctx.HttpContext.Response.Headers.CacheControl = "no-store";
            return await next(ctx);
        });

        trips.MapGet("", (Guid? lineId, DateOnly? serviceDate, string? status, Guid? scheduleVersionId, ClaimsPrincipal p, IAdminTripService s, CancellationToken ct) =>
                s.ListTripsAsync(lineId, serviceDate, status, scheduleVersionId, UserId(p), ct))
            .WithName("ListAdminTrips").WithSummary("UC29 — Danh sách chuyến tàu dự kiến theo ngày/tuyến/trạng thái")
            .Produces<IReadOnlyList<AdminTripResponse>>(StatusCodes.Status200OK);
        trips.MapGet("/{id:guid}", (Guid id, ClaimsPrincipal p, IAdminTripService s, CancellationToken ct) => s.GetTripAsync(id, UserId(p), ct))
            .WithName("GetAdminTrip").WithSummary("UC29 — Chi tiết chuyến, giờ dừng và đoàn tàu đang gán")
            .Produces<AdminTripResponse>(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status404NotFound);
        trips.MapPost("", async (CreateTripRequest req, ClaimsPrincipal p, IAdminTripService s, CancellationToken ct) =>
            { var c = await s.CreateTripAsync(req, UserId(p), ct); return Results.Created($"/api/v1/admin/trips/{c.Id}", c); })
            .AddEndpointFilter<IdempotencyEndpointFilter>().WithName("CreateAdminTrip").WithSummary("UC29 — Tạo chuyến mới, tự sinh giờ dừng theo travel_time_seconds và gán đoàn")
            .Produces<AdminTripResponse>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status409Conflict);
        trips.MapPut("/{id:guid}", async (Guid id, UpdateTripRequest req, ClaimsPrincipal p, IAdminTripService s, CancellationToken ct) =>
                Results.Ok(await s.UpdateTripAsync(id, req, UserId(p), ct)))
            .AddEndpointFilter<IdempotencyEndpointFilter>().WithName("UpdateAdminTrip").WithSummary("UC29/BR-055 — Cập nhật trạng thái chuyến, ga nhận khách, giờ thực tế và hồ sơ tải")
            .Produces<AdminTripResponse>(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status409Conflict);
        trips.MapPost("/{id:guid}/allocations", async (Guid id, AllocateTripTrainsetRequest req, ClaimsPrincipal p, IAdminTripService s, CancellationToken ct) =>
                Results.Ok(await s.AllocateTrainsetAsync(id, req, UserId(p), ct)))
            .AddEndpointFilter<IdempotencyEndpointFilter>().WithName("AllocateAdminTripTrainset").WithSummary("UC29/BR-053/056 — Gán hoặc thay đoàn tàu (chặn trùng thời gian 409)")
            .Produces<AdminTripAllocationResponse>(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    private static string UserId(ClaimsPrincipal p) =>
        p.FindFirstValue(ClaimTypes.NameIdentifier) ?? p.FindFirstValue("sub")
        ?? throw new NetworkAdminException("UNAUTHENTICATED", StatusCodes.Status401Unauthorized, "Missing subject claim.");
}
