using System.Security.Cryptography;
using System.Text;
using Metro.Application.Abstractions.MultiTenancy;
using Metro.Application.Abstractions.Ticketing;
using Metro.Application.Ticketing;
using Metro.Domain.Entities.Passengers;
using Metro.Domain.Entities.Tickets;
using Metro.Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Metro.Infrastructure.Ticketing;

public sealed class TicketService(ApplicationDbContext db, IOperatorContext scope, TicketQrCodec qr, TimeProvider time) : ITicketService
{
    private async Task<Guid> ResolveAsync(string? code, CancellationToken ct)
    {
        if (code is not null && (code.Length is < 1 or > 60 || code.Any(char.IsControl))) throw new TicketException("INVALID_OPERATOR", 400);
        var operators = db.Operators.AsNoTracking().Where(o => o.Status == "ACTIVE");
        if (code is not null) operators = operators.Where(o => o.Code == code);
        else if (scope.CurrentOperatorId.HasValue) operators = operators.Where(o => o.Id == scope.CurrentOperatorId);
        var op = await operators.OrderBy(o => o.CreatedAt).ThenBy(o => o.Id).Select(o => (Guid?)o.Id).FirstOrDefaultAsync(ct);
        scope.CurrentOperatorId = op ?? throw new TicketException("OPERATOR_NOT_FOUND", 404);
        return op.Value;
    }
    private static void Page(int page, int size)
    {
        if (page < 1 || page > 100_000 || size < 1 || size > 100) throw new TicketException("INVALID_PAGINATION", 400);
    }
    private IQueryable<Ticket> Owned(string user)
    {
        var today = TicketRules.LocalDate(time.GetUtcNow());
        return db.Tickets.AsNoTracking().Where(t => t.HolderUserId == user ||
            (t.GuardianUserId == user && db.GuardianLinks.Any(g => g.Id == t.GuardianLinkId &&
                g.GuardianUserId == user && g.ChildUserId == t.HolderUserId && g.Status == GuardianLinkStatus.Active &&
                g.VerificationType == GuardianVerificationType.Guardianship && g.VerifiedAt != null &&
                (g.ValidFrom == null || g.ValidFrom <= today) && (g.ValidTo == null || g.ValidTo >= today))) ||
            (t.GuardianUserId == user && t.Status == "IN_USE" && db.Journeys.Any(j => j.TicketId == t.Id &&
                j.Status == Metro.Domain.Entities.Ticketing.JourneyStatus.Open && j.PairedJourneyId != null &&
                db.Journeys.Any(parent => parent.Id == j.PairedJourneyId && parent.PairedJourneyId == j.Id &&
                    parent.Status == Metro.Domain.Entities.Ticketing.JourneyStatus.Open &&
                    db.Tickets.Any(pt => pt.Id == parent.TicketId && pt.Id == t.RelatedTicketId && pt.HolderUserId == user)))));
    }
    private async Task<TicketView> ViewAsync(Ticket t, CancellationToken ct)
    {
        var job = await db.Database.SqlQuery<JobView>($"""
            SELECT j.status AS "Status",v.eligibility_group AS "EligibilityGroup",clock_timestamp() AS "DbNow"
            FROM metro.ticket_issuance_jobs j LEFT JOIN metro.order_versions v
             ON v.operator_id=j.operator_id AND v.order_id=j.order_id AND v.version={t.OrderVersion}
            WHERE j.operator_id={t.OperatorId} AND j.ticket_id={t.Id}
            """).SingleOrDefaultAsync(ct);
        var current = job?.Status ?? "UNAVAILABLE";
        var now = job?.DbNow ?? time.GetUtcNow().UtcDateTime;
        var exitInProgress = t.Status == "IN_USE" && await db.Journeys.AnyAsync(j => j.TicketId == t.Id && j.Status == Metro.Domain.Entities.Ticketing.JourneyStatus.Open, ct);
        var available = current == "READY" && t.Status is "ACTIVE" or "IN_USE" && (TicketRules.Contains(t.EffectiveFrom, t.ExpiresAt, now) || exitInProgress)
            && await db.TicketCredentials.AnyAsync(c => c.TicketId == t.Id && c.CredentialType == "QR" && c.Status == "ACTIVE" && c.KeyId != null, ct);
        return new(t.Id, t.TicketCode, t.TicketProduct.ProductType,
            t.ExpiresAt <= now && t.Status is "ISSUED" or "ACTIVE" ? "EXPIRED" : t.Status,
            current, t.EffectiveFrom, t.ExpiresAt, t.ServiceDate, t.OriginStationId, t.DestinationStationId,
            available, job?.EligibilityGroup);
    }
    public async Task<TicketPage<TicketView>> GetTicketsAsync(string user, string? operatorCode, int page, int pageSize, CancellationToken ct)
    {
        Page(page, pageSize); await ResolveAsync(operatorCode, ct);
        var query = Owned(user);
        var total = await query.CountAsync(ct);
        var tickets = await query.Include(t => t.TicketProduct).Include(t => t.Order).OrderByDescending(t => t.IssuedAt).ThenBy(t => t.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var views = new List<TicketView>();
        foreach (var t in tickets) views.Add(await ViewAsync(t, ct));
        return new(views, page, pageSize, total);
    }
    public async Task<TicketDetail> GetTicketAsync(string user, Guid id, string? operatorCode, CancellationToken ct)
    {
        await ResolveAsync(operatorCode, ct);
        var t = await Owned(user).Include(t => t.TicketProduct).Include(t => t.Order).SingleOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new TicketException("TICKET_NOT_FOUND", 404);
        var hints = await db.TripSuggestions.AsNoTracking().Where(s => s.OrderId == t.OrderId && s.OrderVersion == t.OrderVersion && s.SupersededAt == null)
            .OrderByDescending(s => s.ComputedAt).Take(3).Select(s => new TripHint(s.SuggestedPlannedTripId, s.SuggestedDepartureAt, s.Status.ToString(), s.UpdatedAt, false)).ToListAsync(ct);
        return new(await ViewAsync(t, ct), hints);
    }
    public async Task<byte[]> GetQrAsync(string user, Guid id, string? operatorCode, CancellationToken ct)
    {
        await ResolveAsync(operatorCode, ct);
        var t = await Owned(user).Include(t => t.TicketProduct).Include(t => t.Order).SingleOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new TicketException("TICKET_NOT_FOUND", 404);
        if (!(await ViewAsync(t, ct)).QrAvailable) throw new TicketException("QR_NOT_AVAILABLE", 409);
        var c = await db.TicketCredentials.AsNoTracking().SingleOrDefaultAsync(c => c.TicketId == id && c.CredentialType == "QR" && c.Status == "ACTIVE" && c.KeyId != null, ct)
            ?? throw new TicketException("QR_NOT_AVAILABLE", 409);
        var token = qr.Token(t.OperatorId, t.CorrelationId, c.Id, c.Version, c.KeyId!);
        if (!TicketQrCodec.Matches(token, c.TokenHash)) throw new TicketException("QR_INTEGRITY_ERROR", 503);
        return TicketQrCodec.Png(token);
    }
    private (DateTime Start, DateTime End) HistoryWindow(DateOnly? from, DateOnly? to)
    {
        var end = to ?? TicketRules.LocalDate(time.GetUtcNow());
        if (end == DateOnly.MaxValue || end.DayNumber < 29) throw new TicketException("INVALID_HISTORY_RANGE", 400);
        var start = from ?? end.AddDays(-29);
        if (start > end || start == DateOnly.MinValue) throw new TicketException("INVALID_HISTORY_RANGE", 400);
        return (TicketRules.Midnight(start), TicketRules.Midnight(end.AddDays(1)));
    }
    public async Task<TicketPage<OrderHistoryItem>> GetOrdersAsync(string user, string? operatorCode, int page, int pageSize, DateOnly? fromDate, DateOnly? toDate, CancellationToken ct)
    {
        Page(page, pageSize); await ResolveAsync(operatorCode, ct);
        var (since, until) = HistoryWindow(fromDate, toDate);
        var query = db.Orders.AsNoTracking().Where(o => o.PayerUserId == user && o.CreatedAt >= since && o.CreatedAt < until);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(o => o.CreatedAt).ThenBy(o => o.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(o => new OrderHistoryItem(o.Id, o.OrderCode, o.OrderVersion, o.Status, o.TotalMinor, o.Currency, o.CreatedAt, o.PaidAt, o.FreeConfirmedAt, o.IssuedAt,
                o.BeneficiaryNameSnapshot, o.Items.Select(i => i.TicketProduct.ProductType).FirstOrDefault() ?? "UNKNOWN",
                o.Versions.OrderByDescending(v => v.Version).Select(v => new OrderVersionView(v.Version, v.Status, v.TicketProduct.ProductType, v.ServiceDate, v.TotalMinor, v.EligibilityGroup)).ToList())).ToListAsync(ct);
        return new(items, page, pageSize, total);
    }
    public async Task<TicketPage<PaymentHistoryItem>> GetPaymentsAsync(string user, string? operatorCode, int page, int pageSize, DateOnly? fromDate, DateOnly? toDate, CancellationToken ct)
    {
        Page(page, pageSize); await ResolveAsync(operatorCode, ct);
        var window = HistoryWindow(fromDate, toDate);
        var since = new DateTimeOffset(window.Start); var until = new DateTimeOffset(window.End);
        var query = db.Payments.AsNoTracking().Where(p => p.CreatedAt >= since && p.CreatedAt < until && db.Orders.Any(o => o.Id == p.OrderId && o.PayerUserId == user));
        var total = await query.CountAsync(ct);
        var payments = await query.OrderByDescending(p => p.CreatedAt).ThenBy(p => p.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var ids = payments.Select(p => p.Id).ToArray();
        var refunds = await db.RefundTransactions.AsNoTracking().Where(r => ids.Contains(r.PaymentId)).OrderBy(r => r.CreatedAt)
            .Select(r => new { r.PaymentId, View = new RefundHistoryItem(r.Id, r.AmountMinor, r.Status.ToString()) }).ToListAsync(ct);
        return new(payments.Select(p => new PaymentHistoryItem(p.Id, p.OrderId, p.OrderVersion, p.PaymentCode, p.AmountMinor,
            p.Currency, p.Status.ToString(), p.ReconciliationStatus.ToString(), p.InitiatedAt, p.SucceededAt,
            refunds.Where(r => r.PaymentId == p.Id).Select(r => r.View).ToArray())).ToArray(), page, pageSize, total);
    }
    public async Task<FreeIssueResult> IssueFreeAsync(string user, Guid orderId, FreeIssueRequest request, string key, string? operatorCode, CancellationToken ct)
    {
        if (request.OrderVersion < 1 || key.Length is < 8 or > 120 || key.Any(c => c < 33 || c > 126)) throw new TicketException("INVALID_ISSUE_REQUEST", 400);
        var op = await ResolveAsync(operatorCode, ct);
        var order = await db.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == orderId, ct) ?? throw new TicketException("ORDER_NOT_FOUND", 404);
        var today = TicketRules.LocalDate(time.GetUtcNow());
        if (!await db.GuardianLinks.AnyAsync(g => g.GuardianUserId == user &&
            g.ChildUserId == order.BeneficiaryUserId && g.Status == GuardianLinkStatus.Active && g.VerifiedAt != null &&
            g.VerificationType == GuardianVerificationType.Guardianship && (g.ValidFrom == null || g.ValidFrom <= today) && (g.ValidTo == null || g.ValidTo >= today), ct))
            throw new TicketException("ISSUE_FORBIDDEN", 403);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"issue-free:{orderId:N}:{request.OrderVersion}")));
        try
        {
            await using var connection = new NpgsqlConnection(db.Database.GetConnectionString()); await connection.OpenAsync(ct);
            await using var tx = await connection.BeginTransactionAsync(ct);
            // Same lock as the SQL paid/free workflows, before locking any order.
            await using (var cmd = TicketSql.Command(connection, tx, "SELECT pg_advisory_xact_lock(hashtextextended(@op::text||':'||@holder,0))", ("op", op), ("holder", order.BeneficiaryUserId!)))
                await cmd.ExecuteNonQueryAsync(ct);
            await using (var cmd = TicketSql.Command(connection, tx, "SELECT order_version=@version AND beneficiary_user_id=@holder FROM metro.orders WHERE operator_id=@op AND id=@id FOR UPDATE", ("op", op), ("id", orderId), ("version", request.OrderVersion), ("holder", order.BeneficiaryUserId!)))
                if (await cmd.ExecuteScalarAsync(ct) is not true) throw new TicketException("ORDER_VERSION_CONFLICT", 409);
            await using (var cmd = TicketSql.Command(connection, tx, "INSERT INTO metro.ticket_issue_requests(operator_id,actor_user_id,request_key,payload_hash,order_id,order_version) VALUES(@op,@actor,@key,@hash,@id,@version) ON CONFLICT DO NOTHING", ("op", op), ("actor", user), ("key", key), ("hash", hash), ("id", orderId), ("version", request.OrderVersion)))
                await cmd.ExecuteNonQueryAsync(ct);
            await using (var cmd = TicketSql.Command(connection, tx, "SELECT payload_hash FROM metro.ticket_issue_requests WHERE operator_id=@op AND actor_user_id=@actor AND request_key=@key", ("op", op), ("actor", user), ("key", key)))
                if ((string)(await cmd.ExecuteScalarAsync(ct))! != hash) throw new TicketException("IDEMPOTENCY_CONFLICT", 409);
            await using (var cmd = TicketSql.Command(connection, tx, "SELECT metro.issue_free_order(@op,@id,@actor)", ("op", op), ("id", orderId), ("actor", user)))
                await cmd.ExecuteScalarAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (PostgresException ex) { throw TicketSql.Error(ex); }
        catch (NpgsqlException) { throw new TicketException("ISSUANCE_UNAVAILABLE", 503); }
        var current = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId, ct);
        var ticketIds = await db.Tickets.AsNoTracking().Where(t => t.OrderId == orderId).Select(t => t.Id).ToListAsync(ct);
        return new(orderId, current.OrderVersion, current.Status, ticketIds);
    }
    private sealed record JobView(string Status, string? EligibilityGroup, DateTime DbNow);
}
