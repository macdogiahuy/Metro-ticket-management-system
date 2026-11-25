using System.Security.Claims;
using Metro.Application.Abstractions.Reports;

namespace Metro.API.Endpoints;

/// <summary>UC33 / BE-15 administrator ticket and revenue reports.</summary>
public static class ReportEndpoints
{
    public static IEndpointRouteBuilder MapReportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/reports").WithTags("Reports").RequireAuthorization("Admin");

        group.MapGet("/revenue", GetRevenueAsync)
            .WithName("GetRevenueReport")
            .Produces<RevenueReportResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/tickets", GetTicketsAsync)
            .WithName("GetTicketReport")
            .Produces<TicketReportResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/revenue/export", ExportRevenueAsync)
            .WithName("ExportRevenueReportCsv")
            .Produces(StatusCodes.Status200OK, contentType: "text/csv")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    /// <summary>Returns revenue from confirmed collection and refund events in UTC+7 business dates.</summary>
    private static async Task<IResult> GetRevenueAsync(ReportQuery query, IReportService reports, ClaimsPrincipal principal, CancellationToken cancellationToken) =>
        Results.Ok(await reports.GetRevenueAsync(query, UserId(principal), cancellationToken));

    /// <summary>Returns ticket issuance status from the durable BE-09 issuance-job queue.</summary>
    private static async Task<IResult> GetTicketsAsync(ReportQuery query, IReportService reports, ClaimsPrincipal principal, CancellationToken cancellationToken) =>
        Results.Ok(await reports.GetTicketsAsync(query, UserId(principal), cancellationToken));

    /// <summary>Exports grouped revenue rows as UTF-8 CSV. A non-sensitive business purpose is required for audit.</summary>
    private static async Task<IResult> ExportRevenueAsync(ReportQuery query, IReportService reports, ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var export = await reports.ExportRevenueCsvAsync(query, UserId(principal), cancellationToken);
        return Results.File(export.Content, "text/csv; charset=utf-8", export.FileName);
    }

    private static string UserId(ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? principal.FindFirstValue("sub")
        ?? throw new ReportException("UNAUTHENTICATED", StatusCodes.Status401Unauthorized, "Missing subject claim.");
}
