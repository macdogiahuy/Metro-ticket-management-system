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
            .WithDescription("Admin only; from/to are inclusive Asia/Ho_Chi_Minh dates (maximum 31). Collections use payment succeeded_at and statuses SUCCEEDED, PARTIALLY_REFUNDED, REFUNDED; reconciliation status does not exclude a successful payment. Valid value is min(payment amount, frozen order version total); excess is the positive difference. Successful refunds use refund succeeded_at. Pending refunds are requests created in the date range that currently have an unresolved request status, not an end-of-period historical balance. Net cashflow equals collected minus successful refunds. lineId uses the last suggestion computed at or before payment success; ticketProductId uses the frozen order version. paymentStatus filters collections, not refund events.")
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
            .WithDescription("Exports the same daily revenue rows and filters as the revenue endpoint. A non-sensitive purpose is required for audit. The output is limited to 10,000 grouped rows and date ranges to 31 inclusive days.")
            .Produces(StatusCodes.Status200OK, contentType: "text/csv")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }

    /// <summary>Returns revenue from confirmed collection and refund events in UTC+7 business dates.</summary>
    private static async Task<IResult> GetRevenueAsync([AsParameters] ReportQuery query, IReportService reports, ClaimsPrincipal principal, CancellationToken cancellationToken) =>
        Results.Ok(await reports.GetRevenueAsync(query, UserId(principal), cancellationToken));

    /// <summary>Returns ticket issuance status from the durable BE-09 issuance-job queue.</summary>
    private static async Task<IResult> GetTicketsAsync([AsParameters] ReportQuery query, IReportService reports, ClaimsPrincipal principal, CancellationToken cancellationToken) =>
        Results.Ok(await reports.GetTicketsAsync(query, UserId(principal), cancellationToken));

    /// <summary>Exports grouped revenue rows as UTF-8 CSV. A non-sensitive business purpose is required for audit.</summary>
    private static async Task<IResult> ExportRevenueAsync([AsParameters] ReportQuery query, IReportService reports, ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var export = await reports.ExportRevenueCsvAsync(query, UserId(principal), cancellationToken);
        return Results.File(export.Content, "text/csv; charset=utf-8", export.FileName);
    }

    private static string UserId(ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? principal.FindFirstValue("sub")
        ?? throw new ReportException("UNAUTHENTICATED", StatusCodes.Status401Unauthorized, "Missing subject claim.");
}
