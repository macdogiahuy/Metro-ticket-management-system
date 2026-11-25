using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Metro.Application.Abstractions.Reports;
using Metro.Domain.Entities.Finance;
using Metro.Domain.Entities.Identity;
using Metro.Infrastructure.Persistence.Context;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Metro.Infrastructure.Reports;

/// <summary>BE-15 reporting over immutable payment versions and BE-09 issuance jobs.</summary>
public sealed class ReportService(
    ApplicationDbContext db,
    IReportOperatorResolver operatorResolver,
    TimeProvider timeProvider) : IReportService
{
    private const string Timezone = "Asia/Ho_Chi_Minh";
    private const int MaxDays = 31;
    private const int MaxPageSize = 100;
    private const int MaxExportRows = 10_000;
    private static readonly TimeZoneInfo VietnamTimeZone = TimeZoneInfo.FindSystemTimeZoneById(Timezone);
    private static readonly HashSet<string> TicketStatuses = new(StringComparer.Ordinal)
        { "ISSUED", "ACTIVE", "IN_USE", "USED", "EXPIRED", "REFUND_PENDING", "REFUNDED", "REVOKED" };
    private static readonly HashSet<string> IssuanceStatuses = new(StringComparer.Ordinal)
        { "PENDING", "READY", "NEEDS_SUPPORT", "CANCELLED" };

    public async Task<RevenueReportResponse> GetRevenueAsync(ReportQuery query, string userId, CancellationToken cancellationToken)
    {
        var range = Validate(query, export: false);
        await operatorResolver.ResolveAsync(userId, cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var result = await BuildRevenueAsync(query, range, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<TicketReportResponse> GetTicketsAsync(ReportQuery query, string userId, CancellationToken cancellationToken)
    {
        var range = Validate(query, export: false);
        await operatorResolver.ResolveAsync(userId, cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);

        var ticketsQuery = db.Tickets.AsNoTracking()
            .Where(t => t.IssuedAt >= range.Start.UtcDateTime && t.IssuedAt < range.EndExclusive.UtcDateTime);
        if (query.TicketProductId is { } productId) ticketsQuery = ticketsQuery.Where(t => t.TicketProductId == productId);
        if (NormalizeTicketStatus(query.TicketStatus) is { } ticketStatus) ticketsQuery = ticketsQuery.Where(t => t.Status == ticketStatus);

        var tickets = await (
            from ticket in ticketsQuery
            join order in db.Orders.AsNoTracking() on new { ticket.OperatorId, OrderId = ticket.OrderId } equals new { order.OperatorId, OrderId = (Guid?)order.Id } into orders
            from order in orders.DefaultIfEmpty()
            select new TicketSource(ticket.Id, ticket.TicketCode, ticket.IssuedAt, ticket.TicketProductId,
                ticket.OrderId, ticket.OrderVersion, ticket.Status, order != null && order.FreeConfirmedAt != null))
            .ToListAsync(cancellationToken);

        var jobs = await db.Database.SqlQuery<TicketJob>($"""
            SELECT ticket_id AS \"TicketId\", status AS \"Status\"
            FROM metro.ticket_issuance_jobs
            WHERE operator_id={db.CurrentOperatorId}
            """).ToListAsync(cancellationToken);
        var jobByTicket = jobs.ToDictionary(x => x.TicketId, x => x.Status, EqualityComparer<Guid>.Default);

        var lineByTicket = await TicketLinesAsync(tickets, range.EndExclusive, cancellationToken);
        var rows = tickets.Select(ticket => new TicketWithLine(ticket,
                jobByTicket.GetValueOrDefault(ticket.Id, "UNAVAILABLE"), lineByTicket.GetValueOrDefault(ticket.Id)))
            .Where(x => query.LineId is null || x.LineId == query.LineId)
            .Where(x => NormalizeIssuanceStatus(query.IssuanceStatus) is not { } status || x.IssuanceStatus == status)
            .OrderByDescending(x => x.Source.IssuedAt).ThenBy(x => x.Source.Id)
            .ToList();

        var paged = rows.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Select(x => new TicketReportRow(x.Source.Id, x.Source.Code, AsOffset(x.Source.IssuedAt), x.Source.TicketProductId,
                x.LineId, x.Source.TicketStatus, x.IssuanceStatus, x.Source.IsFreeOrder)).ToList();
        var totals = new TicketReportTotals(
            rows.LongCount(x => x.IssuanceStatus == "READY"),
            rows.LongCount(x => x.IssuanceStatus is "PENDING" or "NEEDS_SUPPORT"),
            rows.Where(x => x.Source.IsFreeOrder).Select(x => x.Source.OrderId).Where(x => x.HasValue).Distinct().LongCount(),
            rows.Where(x => !x.Source.IsFreeOrder).Select(x => x.Source.OrderId).Where(x => x.HasValue).Distinct().LongCount());

        await transaction.CommitAsync(cancellationToken);
        return new TicketReportResponse(range.From, range.To, Timezone, timeProvider.GetUtcNow(), totals,
            query.Page, query.PageSize, rows.Count, paged);
    }

    public async Task<CsvExport> ExportRevenueCsvAsync(ReportQuery query, string userId, CancellationToken cancellationToken)
    {
        var range = Validate(query, export: true);
        await operatorResolver.ResolveAsync(userId, cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var report = await BuildRevenueAsync(query, range, cancellationToken);
        if (report.Rows.Count > MaxExportRows)
            throw new ReportException("REPORT_EXPORT_LIMIT_EXCEEDED", StatusCodes.Status422UnprocessableEntity,
                $"The export contains more than {MaxExportRows:N0} rows after grouping.");

        var content = BuildCsv(report);
        await RecordExportAsync(query, userId, report, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new CsvExport($"revenue-{range.From:yyyyMMdd}-{range.To:yyyyMMdd}.csv", content);
    }

    private async Task<RevenueReportResponse> BuildRevenueAsync(ReportQuery query, DateRange range, CancellationToken cancellationToken)
    {
        var selectedStatus = ParsePaymentStatus(query.PaymentStatus);
        var collections = db.Payments.AsNoTracking()
            .Where(p => p.SucceededAt != null && p.SucceededAt >= range.Start && p.SucceededAt < range.EndExclusive &&
                (p.Status == PaymentStatus.Succeeded || p.Status == PaymentStatus.PartiallyRefunded || p.Status == PaymentStatus.Refunded));
        if (selectedStatus is { } paymentStatus) collections = collections.Where(p => p.Status == paymentStatus);

        var paymentRows = await (
            from payment in collections
            join version in db.OrderVersions.AsNoTracking()
                on new { payment.OperatorId, payment.OrderId, Version = payment.OrderVersion }
                equals new { version.OperatorId, version.OrderId, Version = (int?)version.Version }
            where payment.OrderVersion != null
            where query.TicketProductId == null || version.TicketProductId == query.TicketProductId
            select new PaymentSource(payment.Id, payment.OrderId, payment.OrderVersion!.Value,
                payment.SucceededAt!.Value, payment.AmountMinor, version.TotalMinor, version.TicketProductId))
            .ToListAsync(cancellationToken);

        var lineByPayment = await PaymentLinesAsync(paymentRows, cancellationToken);
        var filteredPayments = paymentRows
            .Where(p => query.LineId is null || lineByPayment.GetValueOrDefault(p.Id) == query.LineId)
            .ToList();

        var successfulRefunds = await RefundsAsync(range, query, successful: true, cancellationToken);
        var pendingRefunds = await RefundsAsync(range, query, successful: false, cancellationToken);

        var dailyPayments = filteredPayments.GroupBy(p => ToLocalDate(p.SucceededAt));
        var dailyRefunds = successfulRefunds.GroupBy(p => ToLocalDate(p.OccurredAt)).ToDictionary(g => g.Key, g => g.Sum(x => x.AmountMinor));
        var dailyPending = pendingRefunds.GroupBy(p => ToLocalDate(p.OccurredAt)).ToDictionary(g => g.Key, g => g.Sum(x => x.AmountMinor));
        var days = dailyPayments.Select(g => g.Key).Union(dailyRefunds.Keys).Union(dailyPending.Keys).OrderBy(x => x).ToList();
        var rows = days.Select(day =>
        {
            var payments = dailyPayments.FirstOrDefault(g => g.Key == day)?.ToList() ?? [];
            var collected = payments.Sum(x => x.AmountMinor);
            var valid = payments.Sum(x => Math.Min(x.AmountMinor, x.FrozenOrderTotalMinor));
            var excess = payments.Sum(x => Math.Max(0, x.AmountMinor - x.FrozenOrderTotalMinor));
            var refunded = dailyRefunds.GetValueOrDefault(day);
            var pending = dailyPending.GetValueOrDefault(day);
            return new DailyRevenueRow(day, payments.Count, collected, valid, excess, refunded, pending, collected - refunded);
        }).ToList();

        var totals = new RevenueTotals(rows.Sum(x => x.ActualCollectedMinor), rows.Sum(x => x.ValidPaidOrderValueMinor),
            rows.Sum(x => x.IncorrectOrExcessCollectedMinor), rows.Sum(x => x.SuccessfulRefundMinor),
            rows.Sum(x => x.PendingRefundMinor), rows.Sum(x => x.NetCashflowMinor), rows.Sum(x => x.PaymentCount));
        return new RevenueReportResponse(range.From, range.To, Timezone, timeProvider.GetUtcNow(), totals, rows);
    }

    private async Task<List<RefundSource>> RefundsAsync(DateRange range, ReportQuery query, bool successful, CancellationToken cancellationToken)
    {
        if (successful)
        {
            var source =
                from refund in db.RefundTransactions.AsNoTracking()
                join payment in db.Payments.AsNoTracking() on new { refund.OperatorId, refund.PaymentId } equals new { payment.OperatorId, PaymentId = payment.Id }
                join version in db.OrderVersions.AsNoTracking() on new { payment.OperatorId, payment.OrderId, Version = payment.OrderVersion } equals new { version.OperatorId, version.OrderId, Version = (int?)version.Version }
                where refund.Status == RefundTransactionStatus.Succeeded && refund.SucceededAt != null &&
                      refund.SucceededAt >= range.Start && refund.SucceededAt < range.EndExclusive && payment.OrderVersion != null &&
                      (query.TicketProductId == null || version.TicketProductId == query.TicketProductId)
                select new RefundSource(refund.AmountMinor, refund.SucceededAt!.Value, payment.Id);
            var rows = await source.ToListAsync(cancellationToken);
            return await FilterRefundLinesAsync(rows, query.LineId, cancellationToken);
        }

        var pending =
            from request in db.RefundRequests.AsNoTracking()
            join payment in db.Payments.AsNoTracking() on new { request.OperatorId, request.PaymentId } equals new { payment.OperatorId, PaymentId = payment.Id }
            join version in db.OrderVersions.AsNoTracking() on new { payment.OperatorId, payment.OrderId, Version = payment.OrderVersion } equals new { version.OperatorId, version.OrderId, Version = (int?)version.Version }
            where (request.Status == RefundRequestStatus.Submitted || request.Status == RefundRequestStatus.UnderReview ||
                   request.Status == RefundRequestStatus.NeedsInfo || request.Status == RefundRequestStatus.PendingApproval ||
                   request.Status == RefundRequestStatus.Approved || request.Status == RefundRequestStatus.Processing) &&
                  request.CreatedAt >= range.Start && request.CreatedAt < range.EndExclusive && payment.OrderVersion != null &&
                  (query.TicketProductId == null || version.TicketProductId == query.TicketProductId)
            select new RefundSource(request.ApprovedAmountMinor ?? request.RequestedAmountMinor, request.CreatedAt, payment.Id);
        var pendingRows = await pending.ToListAsync(cancellationToken);
        return await FilterRefundLinesAsync(pendingRows, query.LineId, cancellationToken);
    }

    private async Task<List<RefundSource>> FilterRefundLinesAsync(List<RefundSource> rows, Guid? lineId, CancellationToken cancellationToken)
    {
        if (lineId is null || rows.Count == 0) return rows;
        var ids = rows.Select(x => x.PaymentId).Distinct().ToArray();
        var lines = await PaymentLinesAsync(ids, cancellationToken);
        return rows.Where(x => lines.GetValueOrDefault(x.PaymentId) == lineId).ToList();
    }

    private async Task<Dictionary<Guid, Guid?>> PaymentLinesAsync(IReadOnlyCollection<PaymentSource> payments, CancellationToken cancellationToken)
        => await PaymentLinesAsync(payments.Select(x => x.Id).ToArray(), cancellationToken);

    // The latest suggestion computed no later than confirmed collection is the historical route dimension.
    private async Task<Dictionary<Guid, Guid?>> PaymentLinesAsync(IReadOnlyCollection<Guid> paymentIds, CancellationToken cancellationToken)
    {
        if (paymentIds.Count == 0) return [];
        var candidates = await (
            from suggestion in db.TripSuggestions.AsNoTracking()
            join payment in db.Payments.AsNoTracking()
                on new { suggestion.OperatorId, suggestion.OrderId, Version = (int?)suggestion.OrderVersion }
                equals new { payment.OperatorId, payment.OrderId, Version = payment.OrderVersion }
            where paymentIds.Contains(payment.Id) && payment.SucceededAt != null && suggestion.ComputedAt <= payment.SucceededAt
            select new PaymentLineCandidate(payment.Id, suggestion.LineId, suggestion.ComputedAt, suggestion.Id))
            .ToListAsync(cancellationToken);
        return candidates.GroupBy(x => x.PaymentId).ToDictionary(g => g.Key,
            g => g.OrderByDescending(x => x.ComputedAt).ThenByDescending(x => x.SuggestionId).First().LineId);
    }

    private async Task<Dictionary<Guid, Guid?>> TicketLinesAsync(IReadOnlyCollection<TicketSource> tickets, DateTimeOffset endExclusive, CancellationToken cancellationToken)
    {
        var keys = tickets.Where(x => x.OrderId != null && x.OrderVersion != null)
            .Select(x => new TicketOrderKey(x.Id, x.OrderId!.Value, x.OrderVersion!.Value, AsOffset(x.IssuedAt))).ToArray();
        if (keys.Length == 0) return [];
        var orderIds = keys.Select(x => x.OrderId).Distinct().ToArray();
        var suggestions = await db.TripSuggestions.AsNoTracking()
            .Where(s => orderIds.Contains(s.OrderId) && s.ComputedAt < endExclusive)
            .Select(s => new TicketSuggestion(s.OrderId, s.OrderVersion, s.LineId, s.ComputedAt, s.Id))
            .ToListAsync(cancellationToken);
        return keys.ToDictionary(k => k.TicketId, k => suggestions.Where(s => s.OrderId == k.OrderId && s.OrderVersion == k.OrderVersion && s.ComputedAt <= k.IssuedAt)
            .OrderByDescending(s => s.ComputedAt).ThenByDescending(s => s.Id).Select(s => s.LineId).FirstOrDefault());
    }

    private async Task RecordExportAsync(ReportQuery query, string userId, RevenueReportResponse report, CancellationToken cancellationToken)
    {
        var reportJobId = Guid.NewGuid();
        var jobCode = $"RPT-{reportJobId:N}";
        var snapshot = JsonSerializer.Serialize(new
        {
            query.From, query.To, query.LineId, query.TicketProductId, query.PaymentStatus, query.Purpose,
            report.Timezone, report.DataAsOf, rowCount = report.Rows.Count,
        });
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO metro.report_jobs
                (id,operator_id,job_code,report_type,template_version,filters_snapshot,timezone,requested_by_user_id,status,requested_at,started_at,completed_at)
            VALUES ({reportJobId},{db.CurrentOperatorId},{jobCode},{"REVENUE_CSV"},1,{snapshot}::jsonb,{Timezone},{userId},{"SUCCEEDED"},clock_timestamp(),clock_timestamp(),clock_timestamp())
            """, cancellationToken);
        db.AuditLogs.Add(new AuditLog
        {
            OperatorId = db.CurrentOperatorId,
            ActorUserId = userId,
            Action = "REPORT_EXPORT",
            EntityType = "report_jobs",
            EntityId = reportJobId.ToString(),
            ChangedFields = ["filters_snapshot", "row_count", "format"],
            Reason = query.Purpose!.Trim(),
            Outcome = "SUCCEEDED",
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    private static byte[] BuildCsv(RevenueReportResponse report)
    {
        var csv = new StringBuilder();
        csv.AppendLine("businessDate,paymentCount,actualCollectedMinor,validPaidOrderValueMinor,incorrectOrExcessCollectedMinor,successfulRefundMinor,pendingRefundMinor,netCashflowMinor");
        foreach (var row in report.Rows)
        {
            csv.Append(row.BusinessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',')
                .Append(row.PaymentCount).Append(',').Append(row.ActualCollectedMinor).Append(',')
                .Append(row.ValidPaidOrderValueMinor).Append(',').Append(row.IncorrectOrExcessCollectedMinor).Append(',')
                .Append(row.SuccessfulRefundMinor).Append(',').Append(row.PendingRefundMinor).Append(',')
                .Append(row.NetCashflowMinor).AppendLine();
        }
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(csv.ToString());
    }

    private static DateRange Validate(ReportQuery query, bool export)
    {
        if (query.From is null || query.To is null || query.To < query.From || query.To.Value.DayNumber - query.From.Value.DayNumber + 1 > MaxDays)
            throw new ReportException("INVALID_REPORT_DATE_RANGE", StatusCodes.Status400BadRequest,
                "from and to are required and may cover at most 31 inclusive business days.");
        if (query.Page is < 1 or > 100_000 || query.PageSize is < 1 or > MaxPageSize)
            throw new ReportException("INVALID_REPORT_PAGINATION", StatusCodes.Status400BadRequest,
                "page must be positive and pageSize must be between 1 and 100.");
        _ = ParsePaymentStatus(query.PaymentStatus);
        _ = NormalizeTicketStatus(query.TicketStatus);
        _ = NormalizeIssuanceStatus(query.IssuanceStatus);
        if (export && (string.IsNullOrWhiteSpace(query.Purpose) || query.Purpose.Trim().Length is < 3 or > 300))
            throw new ReportException("INVALID_REPORT_EXPORT_PURPOSE", StatusCodes.Status400BadRequest,
                "purpose is required for an export and must contain 3 to 300 characters.");
        return new DateRange(query.From.Value, query.To.Value, StartOfDay(query.From.Value), StartOfDay(query.To.Value.AddDays(1)));
    }

    private static PaymentStatus? ParsePaymentStatus(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Replace("_", string.Empty, StringComparison.Ordinal).Trim();
        if (!Enum.TryParse<PaymentStatus>(normalized, ignoreCase: true, out var status))
            throw new ReportException("INVALID_PAYMENT_STATUS", StatusCodes.Status400BadRequest, "paymentStatus is invalid.");
        return status;
    }

    private static string? NormalizeTicketStatus(string? value) => Normalize(value, TicketStatuses, "INVALID_TICKET_STATUS", "ticketStatus");
    private static string? NormalizeIssuanceStatus(string? value) => Normalize(value, IssuanceStatuses, "INVALID_ISSUANCE_STATUS", "issuanceStatus");

    private static string? Normalize(string? value, HashSet<string> accepted, string code, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim().ToUpperInvariant();
        if (!accepted.Contains(normalized)) throw new ReportException(code, StatusCodes.Status400BadRequest, $"{name} is invalid.");
        return normalized;
    }

    private static DateTimeOffset StartOfDay(DateOnly day) => new(TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue), VietnamTimeZone));
    private static DateOnly ToLocalDate(DateTimeOffset instant) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, VietnamTimeZone).DateTime);
    private static DateTimeOffset AsOffset(DateTime instant) => new(DateTime.SpecifyKind(instant, DateTimeKind.Utc));

    private sealed record DateRange(DateOnly From, DateOnly To, DateTimeOffset Start, DateTimeOffset EndExclusive);
    private sealed record PaymentSource(Guid Id, Guid OrderId, int OrderVersion, DateTimeOffset SucceededAt, long AmountMinor, long FrozenOrderTotalMinor, Guid TicketProductId);
    private sealed record PaymentLineCandidate(Guid PaymentId, Guid? LineId, DateTimeOffset ComputedAt, Guid SuggestionId);
    private sealed record RefundSource(long AmountMinor, DateTimeOffset OccurredAt, Guid PaymentId);
    private sealed record TicketSource(Guid Id, string Code, DateTime IssuedAt, Guid TicketProductId, Guid? OrderId, int? OrderVersion, string TicketStatus, bool IsFreeOrder);
    private sealed record TicketWithLine(TicketSource Source, string IssuanceStatus, Guid? LineId);
    private sealed record TicketOrderKey(Guid TicketId, Guid OrderId, int OrderVersion, DateTimeOffset IssuedAt);
    private sealed record TicketSuggestion(Guid OrderId, int OrderVersion, Guid? LineId, DateTimeOffset ComputedAt, Guid Id);
    private sealed record TicketJob(Guid TicketId, string Status);
}
