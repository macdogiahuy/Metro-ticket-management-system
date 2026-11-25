namespace Metro.Application.Abstractions.Reports;

/// <summary>Filters shared by the BE-15 revenue, ticket and CSV reports.</summary>
public sealed class ReportQuery
{
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public Guid? LineId { get; init; }
    public Guid? TicketProductId { get; init; }
    public string? PaymentStatus { get; init; }
    public string? TicketStatus { get; init; }
    public string? IssuanceStatus { get; init; }
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
    public string? Purpose { get; init; }
}

public sealed record RevenueTotals(
    long ActualCollectedMinor,
    long ValidPaidOrderValueMinor,
    long IncorrectOrExcessCollectedMinor,
    long SuccessfulRefundMinor,
    long PendingRefundMinor,
    long NetCashflowMinor,
    long PaymentCount);

public sealed record DailyRevenueRow(
    DateOnly BusinessDate,
    long PaymentCount,
    long ActualCollectedMinor,
    long ValidPaidOrderValueMinor,
    long IncorrectOrExcessCollectedMinor,
    long SuccessfulRefundMinor,
    long PendingRefundMinor,
    long NetCashflowMinor);

public sealed record RevenueReportResponse(
    DateOnly From,
    DateOnly To,
    string Timezone,
    DateTimeOffset DataAsOf,
    RevenueTotals Totals,
    IReadOnlyList<DailyRevenueRow> Rows);

public sealed record TicketReportTotals(
    long ReadyTicketCount,
    long PendingIssuanceTicketCount,
    long FreeOrderCount,
    long PaidOrderCount);

public sealed record TicketReportRow(
    Guid TicketId,
    string TicketCode,
    DateTimeOffset IssuedAt,
    Guid TicketProductId,
    Guid? LineId,
    string TicketStatus,
    string IssuanceStatus,
    bool IsFreeOrder);

public sealed record TicketReportResponse(
    DateOnly From,
    DateOnly To,
    string Timezone,
    DateTimeOffset DataAsOf,
    TicketReportTotals Totals,
    int Page,
    int PageSize,
    long TotalRows,
    IReadOnlyList<TicketReportRow> Rows);

public sealed record CsvExport(string FileName, byte[] Content);

public interface IReportService
{
    Task<RevenueReportResponse> GetRevenueAsync(ReportQuery query, string userId, CancellationToken cancellationToken);
    Task<TicketReportResponse> GetTicketsAsync(ReportQuery query, string userId, CancellationToken cancellationToken);
    Task<CsvExport> ExportRevenueCsvAsync(ReportQuery query, string userId, CancellationToken cancellationToken);
}

public sealed class ReportException(string code, int status, string message) : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
}
