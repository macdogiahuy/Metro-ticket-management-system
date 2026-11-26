using Metro.Application.Abstractions.Catalog;
using Metro.Application.Abstractions.MultiTenancy;
using Metro.Application.Abstractions.Network;
using Metro.Application.Network;
using Metro.Domain.Entities.Network;
using Metro.Infrastructure.Persistence.Context;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Metro.Infrastructure.Network;

public sealed class AdminScheduleService(
    ApplicationDbContext db,
    IOperatorContext operatorContext,
    ICatalogCache catalogCache) : IAdminScheduleService
{
    public async Task<IReadOnlyList<AdminServiceCalendarResponse>> ListCalendarsAsync(string adminUserId, CancellationToken ct)
    {
        await NetworkAdminHelper.ResolveAdminOperatorAsync(db, operatorContext, adminUserId, ct);
        return (await db.ServiceCalendars.AsNoTracking().OrderByDescending(c => c.Priority).ThenBy(c => c.Code).ToListAsync(ct))
            .Select(ToCalendarResponse).ToArray();
    }

    public async Task<AdminServiceCalendarResponse> CreateCalendarAsync(CreateServiceCalendarRequest req, string adminUserId, CancellationToken ct)
    {
        var opId = await NetworkAdminHelper.ResolveAdminOperatorAsync(db, operatorContext, adminUserId, ct);
        var code = NetworkAdminRules.RequireText(req.Code, 50, "CALENDAR_CODE_REQUIRED", "Calendar code is required (max 50 chars).");
        var name = NetworkAdminRules.RequireText(req.Name, 200, "CALENDAR_NAME_REQUIRED", "Calendar name is required.");
        var dayType = NetworkAdminRules.NormalizeDayType(req.DayType);
        var days = NetworkAdminRules.ValidateDaysOfWeek(req.DaysOfWeek);
        NetworkAdminRules.ValidateDateWindow(req.EffectiveFrom, req.EffectiveTo);
        var status = string.IsNullOrWhiteSpace(req.Status) ? "PUBLISHED" : NetworkAdminRules.NormalizeCalendarStatus(req.Status);
        var now = DateTime.UtcNow;
        var entity = new ServiceCalendar
        {
            OperatorId = opId, Code = code, Name = name, DayType = dayType, DaysOfWeek = days,
            EffectiveFrom = req.EffectiveFrom, EffectiveTo = req.EffectiveTo, Priority = req.Priority,
            Status = status, CreatedByUserId = adminUserId, CreatedAt = now, UpdatedAt = now,
        };
        db.ServiceCalendars.Add(entity);
        NetworkAdminHelper.AddAuditLog(db, opId, adminUserId, "CREATE_SERVICE_CALENDAR", "SERVICE_CALENDAR", entity.Id,
            ["code", "name", "day_type", "days_of_week", "effective_from", "effective_to", "status"], null, new { entity.Id, entity.Code, entity.Status }, req.Reason);
        await SaveMappedAsync("CALENDAR_CODE_DUPLICATE", $"Service calendar '{code}' already exists.", ct: ct);
        return ToCalendarResponse(entity);
    }

    public async Task<IReadOnlyList<AdminScheduleVersionResponse>> ListSchedulesAsync(Guid? lineId, string? status, string adminUserId, CancellationToken ct)
    {
        await NetworkAdminHelper.ResolveAdminOperatorAsync(db, operatorContext, adminUserId, ct);
        var q = db.ScheduleVersions.AsNoTracking().AsQueryable();
        if (lineId.HasValue) q = q.Where(s => s.LineId == lineId.Value);
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(s => s.Status == status.Trim().ToUpperInvariant());
        return (await q.OrderByDescending(s => s.EffectiveFrom).ThenByDescending(s => s.Version).ToListAsync(ct))
            .Select(ToScheduleResponse).ToArray();
    }

    public async Task<AdminScheduleVersionResponse> GetScheduleAsync(Guid scheduleId, string adminUserId, CancellationToken ct)
    {
        await NetworkAdminHelper.ResolveAdminOperatorAsync(db, operatorContext, adminUserId, ct);
        var s = await db.ScheduleVersions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == scheduleId, ct)
            ?? throw new NetworkAdminException("SCHEDULE_NOT_FOUND", StatusCodes.Status404NotFound, "Schedule version was not found.");
        return ToScheduleResponse(s);
    }

    public async Task<AdminScheduleVersionResponse> CreateScheduleAsync(CreateScheduleVersionRequest req, string adminUserId, CancellationToken ct)
    {
        var opId = await NetworkAdminHelper.ResolveAdminOperatorAsync(db, operatorContext, adminUserId, ct);
        var code = NetworkAdminRules.RequireText(req.Code, 60, "SCHEDULE_CODE_REQUIRED", "Schedule code is required (max 60 chars).");
        var (fromUtc, toUtc) = NetworkAdminRules.ValidateEffectiveWindow(req.EffectiveFrom, req.EffectiveTo);
        if (!await db.Lines.AsNoTracking().AnyAsync(l => l.Id == req.LineId, ct))
            throw new NetworkAdminException("LINE_NOT_FOUND", StatusCodes.Status404NotFound, "Line was not found.");
        if (!await db.ServiceCalendars.AsNoTracking().AnyAsync(c => c.Id == req.ServiceCalendarId, ct))
            throw new NetworkAdminException("CALENDAR_NOT_FOUND", StatusCodes.Status404NotFound, "Service calendar was not found.");

        var nextVersion = (await db.ScheduleVersions.AsNoTracking()
            .Where(s => s.LineId == req.LineId && s.ServiceCalendarId == req.ServiceCalendarId).MaxAsync(s => (int?)s.Version, ct) ?? 0) + 1;
        var now = DateTime.UtcNow;
        var entity = new ScheduleVersion
        {
            OperatorId = opId, LineId = req.LineId, ServiceCalendarId = req.ServiceCalendarId, Version = nextVersion,
            Code = code, Status = "DRAFT", EffectiveFrom = fromUtc, EffectiveTo = toUtc,
            CreatedByUserId = adminUserId, DecisionReason = req.Reason?.Trim(), CreatedAt = now, UpdatedAt = now,
        };
        db.ScheduleVersions.Add(entity);
        NetworkAdminHelper.AddAuditLog(db, opId, adminUserId, "CREATE_SCHEDULE_VERSION", "SCHEDULE_VERSION", entity.Id,
            ["line_id", "service_calendar_id", "version", "code", "status", "effective_from", "effective_to"], null, ScheduleSnapshot(entity), req.Reason);
        await SaveMappedAsync("SCHEDULE_CODE_DUPLICATE", $"Schedule version '{code}' already exists.", ct: ct);
        return ToScheduleResponse(entity);
    }

    public async Task<AdminScheduleVersionResponse> ApproveScheduleAsync(Guid scheduleId, ScheduleDecisionRequest req, string adminUserId, CancellationToken ct)
    {
        var opId = await NetworkAdminHelper.ResolveAdminOperatorAsync(db, operatorContext, adminUserId, ct);
        var entity = await db.ScheduleVersions.SingleOrDefaultAsync(s => s.Id == scheduleId, ct)
            ?? throw new NetworkAdminException("SCHEDULE_NOT_FOUND", StatusCodes.Status404NotFound, "Schedule version was not found.");
        if (entity.Status is not ("DRAFT" or "PENDING_APPROVAL"))
            throw new NetworkAdminException("INVALID_SCHEDULE_TRANSITION", StatusCodes.Status409Conflict, $"Cannot approve schedule from status '{entity.Status}'.");
        NetworkAdminRules.EnsureMakerChecker(entity.CreatedByUserId, adminUserId, "schedule version");

        var before = ScheduleSnapshot(entity);
        var now = DateTime.UtcNow;
        entity.Status = "APPROVED";
        entity.ApprovedByUserId = adminUserId;
        entity.ApprovedAt = now;
        entity.DecisionReason = req.Reason?.Trim() ?? entity.DecisionReason;
        entity.UpdatedAt = now;
        NetworkAdminHelper.AddAuditLog(db, opId, adminUserId, "APPROVE_SCHEDULE_VERSION", "SCHEDULE_VERSION", entity.Id,
            ["status", "approved_by_user_id", "approved_at", "decision_reason"], before, ScheduleSnapshot(entity), req.Reason);
        await SaveMappedAsync("SCHEDULE_CONFLICT", "Schedule approval conflicted.", ct: ct);
        return ToScheduleResponse(entity);
    }

    public async Task<AdminScheduleVersionResponse> PublishScheduleAsync(Guid scheduleId, ScheduleDecisionRequest req, string adminUserId, CancellationToken ct)
    {
        var opId = await NetworkAdminHelper.ResolveAdminOperatorAsync(db, operatorContext, adminUserId, ct);
        var entity = await db.ScheduleVersions.SingleOrDefaultAsync(s => s.Id == scheduleId, ct)
            ?? throw new NetworkAdminException("SCHEDULE_NOT_FOUND", StatusCodes.Status404NotFound, "Schedule version was not found.");
        if (entity.Status != "APPROVED")
            throw new NetworkAdminException("INVALID_SCHEDULE_TRANSITION", StatusCodes.Status409Conflict, "Schedule version must be APPROVED before publishing.");
        NetworkAdminRules.EnsureMakerChecker(entity.CreatedByUserId, adminUserId, "schedule version");

        var before = ScheduleSnapshot(entity);
        var now = DateTime.UtcNow;
        entity.Status = "PUBLISHED";
        entity.PublishedByUserId = adminUserId;
        entity.PublishedAt = now;
        entity.DecisionReason = req.Reason?.Trim() ?? entity.DecisionReason;
        entity.UpdatedAt = now;
        NetworkAdminHelper.AddAuditLog(db, opId, adminUserId, "PUBLISH_SCHEDULE_VERSION", "SCHEDULE_VERSION", entity.Id,
            ["status", "published_by_user_id", "published_at", "decision_reason"], before, ScheduleSnapshot(entity), req.Reason);
        await SaveMappedAsync("SCHEDULE_CONFLICT", "Schedule publish conflicted.",
            "SCHEDULE_WINDOW_OVERLAP", "Published schedule version overlaps an existing published schedule window for this line.", ct);
        await catalogCache.InvalidateOperatorAsync(opId, ct);
        return ToScheduleResponse(entity);
    }

    private async Task SaveMappedAsync(string dupCode, string dupMsg, string? exCode = null, string? exMsg = null, CancellationToken ct = default)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) { throw NetworkAdminHelper.MapPostgresException(ex, dupCode, dupMsg, exCode, exMsg); }
    }

    private static object ScheduleSnapshot(ScheduleVersion s) => new { s.Id, s.LineId, s.ServiceCalendarId, s.Version, s.Code, s.Status, s.EffectiveFrom, s.EffectiveTo, s.ApprovedByUserId, s.PublishedByUserId };
    private static AdminServiceCalendarResponse ToCalendarResponse(ServiceCalendar c) => new(c.Id, c.Code, c.Name, c.DayType, c.DaysOfWeek, c.EffectiveFrom, c.EffectiveTo, c.Priority, c.Status, c.CreatedAt, c.UpdatedAt);
    private static AdminScheduleVersionResponse ToScheduleResponse(ScheduleVersion s) => new(s.Id, s.LineId, s.ServiceCalendarId, s.Version, s.Code, s.Status, s.EffectiveFrom, s.EffectiveTo, s.CreatedByUserId, s.ApprovedByUserId, s.PublishedByUserId, s.ApprovedAt, s.PublishedAt, s.DecisionReason, s.CreatedAt, s.UpdatedAt);
}
