using System.Security.Claims;
using Metro.API.Filters;
using Metro.Application.Abstractions.Network;

namespace Metro.API.Endpoints;

public static class AdminScheduleEndpoints
{
    public static IEndpointRouteBuilder MapAdminScheduleEndpoints(this IEndpointRouteBuilder app)
    {
        var schedules = app.MapGroup("/api/v1/admin/schedules")
            .WithTags("Admin — Schedules (BE-13)")
            .RequireAuthorization("Admin");
        schedules.AddEndpointFilter(async (ctx, next) =>
        {
            ctx.HttpContext.Response.Headers.CacheControl = "no-store";
            return await next(ctx);
        });

        schedules.MapGet("/calendars", (ClaimsPrincipal p, IAdminScheduleService s, CancellationToken ct) => s.ListCalendarsAsync(UserId(p), ct))
            .WithName("ListAdminServiceCalendars").WithSummary("UC29 — Danh sách lịch dịch vụ (weekday/weekend/holiday)")
            .Produces<IReadOnlyList<AdminServiceCalendarResponse>>(StatusCodes.Status200OK);
        schedules.MapPost("/calendars", async (CreateServiceCalendarRequest req, ClaimsPrincipal p, IAdminScheduleService s, CancellationToken ct) =>
            { var c = await s.CreateCalendarAsync(req, UserId(p), ct); return Results.Created($"/api/v1/admin/schedules/calendars/{c.Id}", c); })
            .AddEndpointFilter<IdempotencyEndpointFilter>().WithName("CreateAdminServiceCalendar").WithSummary("UC29 — Tạo lịch dịch vụ")
            .Produces<AdminServiceCalendarResponse>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status409Conflict);

        schedules.MapGet("", (Guid? lineId, string? status, ClaimsPrincipal p, IAdminScheduleService s, CancellationToken ct) => s.ListSchedulesAsync(lineId, status, UserId(p), ct))
            .WithName("ListAdminSchedules").WithSummary("UC29 — Danh sách phiên bản lịch chạy tàu")
            .Produces<IReadOnlyList<AdminScheduleVersionResponse>>(StatusCodes.Status200OK);
        schedules.MapGet("/{id:guid}", (Guid id, ClaimsPrincipal p, IAdminScheduleService s, CancellationToken ct) => s.GetScheduleAsync(id, UserId(p), ct))
            .WithName("GetAdminSchedule").WithSummary("UC29 — Chi tiết phiên bản lịch chạy tàu")
            .Produces<AdminScheduleVersionResponse>(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status404NotFound);
        schedules.MapPost("", async (CreateScheduleVersionRequest req, ClaimsPrincipal p, IAdminScheduleService s, CancellationToken ct) =>
            { var c = await s.CreateScheduleAsync(req, UserId(p), ct); return Results.Created($"/api/v1/admin/schedules/{c.Id}", c); })
            .AddEndpointFilter<IdempotencyEndpointFilter>().WithName("CreateAdminSchedule").WithSummary("UC29 — Tạo phiên bản lịch DRAFT theo hiệu lực [from, to)")
            .Produces<AdminScheduleVersionResponse>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status409Conflict);
        schedules.MapPost("/{id:guid}/approve", async (Guid id, ScheduleDecisionRequest req, ClaimsPrincipal p, IAdminScheduleService s, CancellationToken ct) =>
                Results.Ok(await s.ApproveScheduleAsync(id, req, UserId(p), ct)))
            .AddEndpointFilter<IdempotencyEndpointFilter>().WithName("ApproveAdminSchedule").WithSummary("UC29 — Phê duyệt phiên bản lịch (maker-checker)")
            .Produces<AdminScheduleVersionResponse>(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status409Conflict);
        schedules.MapPost("/{id:guid}/publish", async (Guid id, ScheduleDecisionRequest req, ClaimsPrincipal p, IAdminScheduleService s, CancellationToken ct) =>
                Results.Ok(await s.PublishScheduleAsync(id, req, UserId(p), ct)))
            .AddEndpointFilter<IdempotencyEndpointFilter>().WithName("PublishAdminSchedule").WithSummary("UC29 — Công bố phiên bản lịch (chặn chồng lấn hiệu lực 409)")
            .Produces<AdminScheduleVersionResponse>(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    private static string UserId(ClaimsPrincipal p) =>
        p.FindFirstValue(ClaimTypes.NameIdentifier) ?? p.FindFirstValue("sub")
        ?? throw new NetworkAdminException("UNAUTHENTICATED", StatusCodes.Status401Unauthorized, "Missing subject claim.");
}
