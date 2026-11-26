using Metro.Application.Abstractions.Catalog;
using Metro.Application.Abstractions.MultiTenancy;
using Metro.Application.Abstractions.Network;
using Metro.Application.Network;
using Metro.Infrastructure.Persistence.Context;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Metro.Infrastructure.Network;

public sealed class AdminNoticeService(
    ApplicationDbContext db,
    IOperatorContext operatorContext,
    ICatalogCache catalogCache) : IAdminNoticeService
{
    public async Task<IReadOnlyList<AdminServiceNoticeResponse>> ListAsync(string? status, string? audience, bool activeOnly, string adminUserId, CancellationToken ct)
    {
        var opId = await NetworkAdminHelper.ResolveAdminOperatorAsync(db, operatorContext, adminUserId, ct);
        var st = string.IsNullOrWhiteSpace(status) ? null : status.Trim().ToUpperInvariant();
        var aud = string.IsNullOrWhiteSpace(audience) ? null : audience.Trim().ToUpperInvariant();
        var now = DateTime.UtcNow;
        var rows = await db.Database.SqlQuery<NoticeRow>($"""
            SELECT id AS "Id", code AS "Code", incident_id AS "IncidentId", audience AS "Audience", title AS "Title", content AS "Content",
                   status AS "Status", emergency_template AS "EmergencyTemplate", valid_from AS "ValidFrom", valid_to AS "ValidTo",
                   created_by_user_id AS "CreatedByUserId", approved_by_user_id AS "ApprovedByUserId", published_by_user_id AS "PublishedByUserId",
                   approved_at AS "ApprovedAt", published_at AS "PublishedAt", decision_reason AS "DecisionReason",
                   created_at AS "CreatedAt", updated_at AS "UpdatedAt"
            FROM metro.service_notices
            WHERE operator_id = {opId}
              AND ({st}::text IS NULL OR status = {st})
              AND ({aud}::text IS NULL OR UPPER(audience) = {aud})
              AND (NOT {activeOnly} OR (status = 'PUBLISHED' AND valid_from <= {now} AND (valid_to IS NULL OR valid_to > {now})))
            ORDER BY COALESCE(published_at, created_at) DESC
            LIMIT 200
            """).ToListAsync(ct);
        return rows.Select(r => ToResponse(r, now)).ToArray();
    }

    public async Task<AdminServiceNoticeResponse> GetAsync(Guid noticeId, string adminUserId, CancellationToken ct)
    {
        var opId = await NetworkAdminHelper.ResolveAdminOperatorAsync(db, operatorContext, adminUserId, ct);
        return ToResponse(await LoadRowAsync(opId, noticeId, ct), DateTime.UtcNow);
    }

    public async Task<AdminServiceNoticeResponse> CreateAsync(CreateServiceNoticeRequest req, string adminUserId, CancellationToken ct)
    {
        var opId = await NetworkAdminHelper.ResolveAdminOperatorAsync(db, operatorContext, adminUserId, ct);
        var code = NetworkAdminRules.RequireText(req.Code, 50, "NOTICE_CODE_REQUIRED", "Notice code is required (max 50 chars).");
        var audience = NetworkAdminRules.RequireText(req.Audience, 40, "NOTICE_AUDIENCE_REQUIRED", "Audience is required (e.g., PUBLIC, INTERNAL).").ToUpperInvariant();
        var title = NetworkAdminRules.RequireText(req.Title, 240, "NOTICE_TITLE_REQUIRED", "Notice title is required.");
        var content = NetworkAdminRules.RequireText(req.Content, 4000, "NOTICE_CONTENT_REQUIRED", "Notice content is required.");
        var (fromUtc, toUtc) = NetworkAdminRules.ValidateEffectiveWindow(req.ValidFrom, req.ValidTo);
        await EnsureIncidentExistsAsync(opId, req.IncidentId, ct);

        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await ExecuteMappedAsync("NOTICE_CODE_DUPLICATE", $"Service notice '{code}' already exists.", async () =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO metro.service_notices
                    (id, operator_id, incident_id, code, audience, title, content, status, emergency_template, valid_from, valid_to, created_by_user_id, decision_reason, created_at, updated_at)
                VALUES ({id}, {opId}, {req.IncidentId}, {code}, {audience}, {title}, {content}, 'DRAFT', {req.EmergencyTemplate}, {fromUtc}, {toUtc}, {adminUserId}, {req.Reason?.Trim()}, {now}, {now})
                """, ct);
            NetworkAdminHelper.AddAuditLog(db, opId, adminUserId, "CREATE_SERVICE_NOTICE", "SERVICE_NOTICE", id,
                ["code", "audience", "title", "status", "emergency_template", "valid_from", "valid_to"], null, new { id, code, audience, req.EmergencyTemplate, fromUtc, toUtc }, req.Reason);
            await db.SaveChangesAsync(ct);
        }, ct);
        return ToResponse(await LoadRowAsync(opId, id, ct), DateTime.UtcNow);
    }

    public async Task<AdminServiceNoticeResponse> UpdateAsync(Guid noticeId, UpdateServiceNoticeRequest req, string adminUserId, CancellationToken ct)
    {
        var opId = await NetworkAdminHelper.ResolveAdminOperatorAsync(db, operatorContext, adminUserId, ct);
        var existing = await LoadRowAsync(opId, noticeId, ct);
        if (existing.Status is not ("DRAFT" or "PENDING_APPROVAL"))
            throw new NetworkAdminException("NOTICE_IMMUTABLE", StatusCodes.Status409Conflict, $"Only DRAFT/PENDING_APPROVAL notices can be edited (current: '{existing.Status}').");
        var audience = NetworkAdminRules.RequireText(req.Audience, 40, "NOTICE_AUDIENCE_REQUIRED", "Audience is required.").ToUpperInvariant();
        var title = NetworkAdminRules.RequireText(req.Title, 240, "NOTICE_TITLE_REQUIRED", "Notice title is required.");
        var content = NetworkAdminRules.RequireText(req.Content, 4000, "NOTICE_CONTENT_REQUIRED", "Notice content is required.");
        var (fromUtc, toUtc) = NetworkAdminRules.ValidateEffectiveWindow(req.ValidFrom, req.ValidTo);
        await EnsureIncidentExistsAsync(opId, req.IncidentId, ct);

        var now = DateTime.UtcNow;
        await ExecuteMappedAsync("NOTICE_CONFLICT", "Service notice update conflicted.", async () =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE metro.service_notices
                SET incident_id = {req.IncidentId}, audience = {audience}, title = {title}, content = {content},
                    emergency_template = {req.EmergencyTemplate}, valid_from = {fromUtc}, valid_to = {toUtc},
                    decision_reason = COALESCE({req.Reason?.Trim()}, decision_reason), updated_at = {now}
                WHERE operator_id = {opId} AND id = {noticeId}
                """, ct);
            NetworkAdminHelper.AddAuditLog(db, opId, adminUserId, "UPDATE_SERVICE_NOTICE", "SERVICE_NOTICE", noticeId,
                ["audience", "title", "content", "emergency_template", "valid_from", "valid_to"], existing, new { noticeId, audience, req.EmergencyTemplate, fromUtc, toUtc }, req.Reason);
            await db.SaveChangesAsync(ct);
        }, ct);
        return ToResponse(await LoadRowAsync(opId, noticeId, ct), DateTime.UtcNow);
    }

    public async Task<AdminServiceNoticeResponse> ApproveAsync(Guid noticeId, ServiceNoticeDecisionRequest req, string adminUserId, CancellationToken ct)
    {
        var opId = await NetworkAdminHelper.ResolveAdminOperatorAsync(db, operatorContext, adminUserId, ct);
        var existing = await LoadRowAsync(opId, noticeId, ct);
        if (existing.Status is not ("DRAFT" or "PENDING_APPROVAL"))
            throw new NetworkAdminException("INVALID_NOTICE_TRANSITION", StatusCodes.Status409Conflict, $"Cannot approve notice from status '{existing.Status}'.");
        NetworkAdminRules.EnsureNoticeMakerChecker(existing.EmergencyTemplate, existing.CreatedByUserId, adminUserId);

        var now = DateTime.UtcNow;
        await ExecuteMappedAsync("NOTICE_CONFLICT", "Service notice approval conflicted.", async () =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE metro.service_notices SET status = 'APPROVED', approved_by_user_id = {adminUserId}, approved_at = {now},
                    decision_reason = COALESCE({req.Reason?.Trim()}, decision_reason), updated_at = {now}
                WHERE operator_id = {opId} AND id = {noticeId}
                """, ct);
            NetworkAdminHelper.AddAuditLog(db, opId, adminUserId, "APPROVE_SERVICE_NOTICE", "SERVICE_NOTICE", noticeId,
                ["status", "approved_by_user_id", "approved_at"], existing, new { noticeId, Status = "APPROVED" }, req.Reason);
            await db.SaveChangesAsync(ct);
        }, ct);
        return ToResponse(await LoadRowAsync(opId, noticeId, ct), DateTime.UtcNow);
    }

    public async Task<AdminServiceNoticeResponse> PublishAsync(Guid noticeId, ServiceNoticeDecisionRequest req, string adminUserId, CancellationToken ct)
    {
        var opId = await NetworkAdminHelper.ResolveAdminOperatorAsync(db, operatorContext, adminUserId, ct);
        var existing = await LoadRowAsync(opId, noticeId, ct);
        var canPublish = existing.Status == "APPROVED" || (existing.EmergencyTemplate && existing.Status is "DRAFT" or "PENDING_APPROVAL");
        if (!canPublish)
            throw new NetworkAdminException("INVALID_NOTICE_TRANSITION", StatusCodes.Status409Conflict, "Non-emergency service notices must be APPROVED before publishing.");
        NetworkAdminRules.EnsureNoticeMakerChecker(existing.EmergencyTemplate, existing.CreatedByUserId, adminUserId);
        var now = DateTime.UtcNow;
        NetworkAdminRules.ValidateNoticePublishable(existing.ValidFrom, existing.ValidTo, now);

        await ExecuteMappedAsync("NOTICE_CONFLICT", "Service notice publish conflicted.", async () =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE metro.service_notices SET status = 'PUBLISHED', published_by_user_id = {adminUserId}, published_at = {now},
                    decision_reason = COALESCE({req.Reason?.Trim()}, decision_reason), updated_at = {now}
                WHERE operator_id = {opId} AND id = {noticeId}
                """, ct);
            NetworkAdminHelper.AddAuditLog(db, opId, adminUserId, "PUBLISH_SERVICE_NOTICE", "SERVICE_NOTICE", noticeId,
                ["status", "published_by_user_id", "published_at"], existing, new { noticeId, Status = "PUBLISHED", PublishedAt = now }, req.Reason);
            await db.SaveChangesAsync(ct);
        }, ct);
        await catalogCache.InvalidateOperatorAsync(opId, ct);
        return ToResponse(await LoadRowAsync(opId, noticeId, ct), DateTime.UtcNow);
    }

    public async Task<AdminServiceNoticeResponse> CancelOrExpireAsync(Guid noticeId, ServiceNoticeDecisionRequest req, string adminUserId, CancellationToken ct)
    {
        var opId = await NetworkAdminHelper.ResolveAdminOperatorAsync(db, operatorContext, adminUserId, ct);
        var existing = await LoadRowAsync(opId, noticeId, ct);
        if (existing.Status is "CANCELLED" or "EXPIRED")
            throw new NetworkAdminException("INVALID_NOTICE_TRANSITION", StatusCodes.Status409Conflict, $"Notice is already '{existing.Status}'.");
        var nextStatus = req.Expire ? "EXPIRED" : "CANCELLED";
        var now = DateTime.UtcNow;

        await ExecuteMappedAsync("NOTICE_CONFLICT", "Service notice cancellation conflicted.", async () =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE metro.service_notices SET status = {nextStatus},
                    decision_reason = COALESCE({req.Reason?.Trim()}, decision_reason), updated_at = {now}
                WHERE operator_id = {opId} AND id = {noticeId}
                """, ct);
            NetworkAdminHelper.AddAuditLog(db, opId, adminUserId, req.Expire ? "EXPIRE_SERVICE_NOTICE" : "CANCEL_SERVICE_NOTICE", "SERVICE_NOTICE", noticeId,
                ["status", "decision_reason"], existing, new { noticeId, Status = nextStatus }, req.Reason);
            await db.SaveChangesAsync(ct);
        }, ct);
        await catalogCache.InvalidateOperatorAsync(opId, ct);
        return ToResponse(await LoadRowAsync(opId, noticeId, ct), DateTime.UtcNow);
    }

    private async Task EnsureIncidentExistsAsync(Guid opId, Guid? incidentId, CancellationToken ct)
    {
        if (!incidentId.HasValue) return;
        var exists = await db.Database.SqlQuery<bool>($"""SELECT EXISTS (SELECT 1 FROM metro.incidents WHERE operator_id = {opId} AND id = {incidentId.Value}) AS "Value" """).SingleAsync(ct);
        if (!exists) throw new NetworkAdminException("INCIDENT_NOT_FOUND", StatusCodes.Status404NotFound, "Referenced incident was not found.");
    }

    private async Task<NoticeRow> LoadRowAsync(Guid opId, Guid id, CancellationToken ct) =>
        await db.Database.SqlQuery<NoticeRow>($"""
            SELECT id AS "Id", code AS "Code", incident_id AS "IncidentId", audience AS "Audience", title AS "Title", content AS "Content",
                   status AS "Status", emergency_template AS "EmergencyTemplate", valid_from AS "ValidFrom", valid_to AS "ValidTo",
                   created_by_user_id AS "CreatedByUserId", approved_by_user_id AS "ApprovedByUserId", published_by_user_id AS "PublishedByUserId",
                   approved_at AS "ApprovedAt", published_at AS "PublishedAt", decision_reason AS "DecisionReason",
                   created_at AS "CreatedAt", updated_at AS "UpdatedAt"
            FROM metro.service_notices WHERE operator_id = {opId} AND id = {id}
            """).SingleOrDefaultAsync(ct)
        ?? throw new NetworkAdminException("NOTICE_NOT_FOUND", StatusCodes.Status404NotFound, "Service notice was not found.");

    private Task ExecuteMappedAsync(string dupCode, string dupMsg, Func<Task> action, CancellationToken ct) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            try { await action(); await tx.CommitAsync(ct); }
            catch (NetworkAdminException) { throw; }
            catch (Exception ex) { throw NetworkAdminHelper.MapPostgresException(ex, dupCode, dupMsg); }
        });

    private static AdminServiceNoticeResponse ToResponse(NoticeRow r, DateTime now) => new(
        r.Id, r.Code, r.IncidentId, r.Audience, r.Title, r.Content, r.Status, r.EmergencyTemplate,
        r.ValidFrom, r.ValidTo, r.Status == "PUBLISHED" && r.ValidFrom <= now && (r.ValidTo is null || r.ValidTo > now),
        r.CreatedByUserId, r.ApprovedByUserId, r.PublishedByUserId, r.ApprovedAt, r.PublishedAt, r.DecisionReason, r.CreatedAt, r.UpdatedAt);

    private sealed record NoticeRow(
        Guid Id, string Code, Guid? IncidentId, string Audience, string Title, string Content, string Status,
        bool EmergencyTemplate, DateTime ValidFrom, DateTime? ValidTo, string CreatedByUserId, string? ApprovedByUserId,
        string? PublishedByUserId, DateTime? ApprovedAt, DateTime? PublishedAt, string? DecisionReason, DateTime CreatedAt, DateTime UpdatedAt);
}
