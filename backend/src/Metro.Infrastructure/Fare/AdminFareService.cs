using Metro.Application.Abstractions.Fare;
using Metro.Application.Abstractions.MultiTenancy;
using Metro.Application.Fare;
using Metro.Domain.Common;
using Metro.Domain.Entities.Orders;
using Metro.Infrastructure.Persistence.Context;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Metro.Infrastructure.Fare;

public sealed class AdminFareService(ApplicationDbContext db, IOperatorContext operatorContext) : IAdminFareService
{
    public async Task<IReadOnlyList<AdminFarePolicyVersionResponse>> ListVersionsAsync(string? status, string? scopeKey, string adminUserId, CancellationToken ct)
    {
        await AdminAuditHelper.ResolveAdminOperatorAsync(db, operatorContext, adminUserId, ct);
        var query = db.FarePolicyVersions.AsNoTracking().Include(v => v.MatrixEntries).AsQueryable();
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(v => v.Status == status.Trim().ToUpperInvariant());
        if (!string.IsNullOrWhiteSpace(scopeKey)) query = query.Where(v => v.ScopeKey == scopeKey.Trim());
        var list = await query.OrderByDescending(v => v.EffectiveFrom).ThenByDescending(v => v.Version).ToListAsync(ct);
        return list.Select(ToVersionResponse).ToArray();
    }

    public async Task<AdminFarePolicyVersionResponse> GetVersionAsync(Guid versionId, string adminUserId, CancellationToken ct)
    {
        await AdminAuditHelper.ResolveAdminOperatorAsync(db, operatorContext, adminUserId, ct);
        var version = await db.FarePolicyVersions.AsNoTracking().Include(v => v.MatrixEntries)
            .SingleOrDefaultAsync(v => v.Id == versionId, ct)
            ?? throw new FareException("FARE_POLICY_NOT_FOUND", StatusCodes.Status404NotFound, "Fare policy version was not found.");
        return ToVersionResponse(version);
    }

    public async Task<AdminFarePolicyVersionResponse> CreateVersionAsync(CreateFarePolicyVersionRequest request, string adminUserId, CancellationToken ct)
    {
        var opId = await AdminAuditHelper.ResolveAdminOperatorAsync(db, operatorContext, adminUserId, ct);
        var code = RequireText(request.Code, 60, "FARE_VERSION_CODE_REQUIRED", "Fare policy version code is required (max 60 chars).");
        var scope = RequireText(request.ScopeKey, 100, "SCOPE_KEY_REQUIRED", "scopeKey is required (max 100 chars).");
        var (fromUtc, toUtc) = ValidateWindow(request.EffectiveFrom, request.EffectiveTo);

        var nextVersion = (await db.FarePolicyVersions.AsNoTracking()
            .Where(v => v.ScopeKey == scope).MaxAsync(v => (int?)v.Version, ct) ?? 0) + 1;
        var now = DateTime.UtcNow;
        var entity = new FarePolicyVersion
        {
            OperatorId = opId, Code = code, Version = nextVersion, ScopeKey = scope, Status = "DRAFT",
            Currency = "VND", EffectiveFrom = fromUtc, EffectiveTo = toUtc, CreatedByUserId = adminUserId,
            DecisionReason = request.Reason?.Trim(), CreatedAt = now, UpdatedAt = now,
        };
        db.FarePolicyVersions.Add(entity);
        AdminAuditHelper.AddAuditLog(db, opId, adminUserId, "CREATE_FARE_POLICY_VERSION", "FARE_POLICY_VERSION", entity.Id,
            ["code", "version", "scope_key", "status", "effective_from", "effective_to"], null, VersionSnapshot(entity), request.Reason);
        await SaveMappedAsync("FARE_VERSION_CONFLICT", $"Fare policy version '{code}' already exists.", ct: ct);
        return ToVersionResponse(entity);
    }

    public async Task<AdminFareMatrixEntryResponse> AddMatrixEntryAsync(Guid versionId, UpsertFareMatrixEntryRequest request, string adminUserId, CancellationToken ct)
    {
        var opId = await AdminAuditHelper.ResolveAdminOperatorAsync(db, operatorContext, adminUserId, ct);
        var version = await db.FarePolicyVersions.SingleOrDefaultAsync(v => v.Id == versionId, ct)
            ?? throw new FareException("FARE_POLICY_NOT_FOUND", StatusCodes.Status404NotFound, "Fare policy version was not found.");
        if (version.Status != "DRAFT")
            throw new FareException("FARE_VERSION_IMMUTABLE", StatusCodes.Status409Conflict, "Only DRAFT fare policy versions can be edited (BR-018).");

        var product = await db.TicketProducts.AsNoTracking().SingleOrDefaultAsync(p => p.Id == request.TicketProductId, ct)
            ?? throw new FareException("PRODUCT_NOT_FOUND", StatusCodes.Status404NotFound, "Ticket product was not found.");
        var channel = FareRules.NormalizePaymentChannel(request.PaymentChannel);
        var group = FareRules.NormalizeEligibilityGroup(request.EligibilityGroup);

        if (request.AmountMinor < 0)
            throw new FareException("INVALID_AMOUNT", StatusCodes.Status400BadRequest, "amountMinor must be non-negative.");
        FareRules.ValidatePaidRange(request.AmountMinor, isFreeTicket: group == "CHILD_UNDER_6" && request.AmountMinor == 0);

        if ((request.OriginStationId is null) != (request.DestinationStationId is null))
            throw new FareException("INCOMPLETE_OD_PAIR", StatusCodes.Status400BadRequest, "Both originStationId and destinationStationId must be set together or both null.");
        if (request.OriginStationId.HasValue && request.OriginStationId == request.DestinationStationId)
            throw new FareException("SAME_STATION", StatusCodes.Status400BadRequest, "Origin and destination stations must be different.");
        if (product.ProductType != "SINGLE_TRIP" && (request.LineId.HasValue || request.OriginStationId.HasValue))
            throw new FareException("OD_NOT_ALLOWED_FOR_PERIODIC", StatusCodes.Status400BadRequest, "Periodic passes must use NULL line/origin/destination scope.");

        var entry = new FareMatrixEntry
        {
            OperatorId = opId, FarePolicyVersionId = version.Id, TicketProductId = product.Id,
            LineId = request.LineId, OriginStationId = request.OriginStationId, DestinationStationId = request.DestinationStationId,
            PaymentChannel = channel, EligibilityGroup = group, AmountMinor = request.AmountMinor, Sellable = request.Sellable,
        };
        db.FareMatrixEntries.Add(entry);
        AdminAuditHelper.AddAuditLog(db, opId, adminUserId, "ADD_FARE_MATRIX_ENTRY", "FARE_MATRIX_ENTRY", entry.Id,
            ["fare_policy_version_id", "ticket_product_id", "line_id", "origin_station_id", "destination_station_id", "payment_channel", "eligibility_group", "amount_minor", "sellable"],
            null, ToEntryResponse(entry), request.Reason);
        await SaveMappedAsync("FARE_MATRIX_DUPLICATE", "A fare matrix entry with the same NULL-safe key already exists in this version.", ct: ct);
        return ToEntryResponse(entry);
    }

    public async Task DeleteMatrixEntryAsync(Guid versionId, Guid entryId, string? reason, string adminUserId, CancellationToken ct)
    {
        var opId = await AdminAuditHelper.ResolveAdminOperatorAsync(db, operatorContext, adminUserId, ct);
        var version = await db.FarePolicyVersions.SingleOrDefaultAsync(v => v.Id == versionId, ct)
            ?? throw new FareException("FARE_POLICY_NOT_FOUND", StatusCodes.Status404NotFound, "Fare policy version was not found.");
        if (version.Status != "DRAFT")
            throw new FareException("FARE_VERSION_IMMUTABLE", StatusCodes.Status409Conflict, "Only DRAFT fare policy versions can be edited (BR-018).");
        var entry = await db.FareMatrixEntries.SingleOrDefaultAsync(e => e.FarePolicyVersionId == versionId && e.Id == entryId, ct)
            ?? throw new FareException("FARE_MATRIX_ENTRY_NOT_FOUND", StatusCodes.Status404NotFound, "Fare matrix entry was not found.");

        db.FareMatrixEntries.Remove(entry);
        AdminAuditHelper.AddAuditLog(db, opId, adminUserId, "DELETE_FARE_MATRIX_ENTRY", "FARE_MATRIX_ENTRY", entry.Id,
            ["deleted"], ToEntryResponse(entry), null, reason);
        await db.SaveChangesAsync(ct);
    }

    public async Task<AdminFarePolicyVersionResponse> ApproveVersionAsync(Guid versionId, FarePolicyDecisionRequest request, string adminUserId, CancellationToken ct)
    {
        var opId = await AdminAuditHelper.ResolveAdminOperatorAsync(db, operatorContext, adminUserId, ct);
        var version = await db.FarePolicyVersions.Include(v => v.MatrixEntries).SingleOrDefaultAsync(v => v.Id == versionId, ct)
            ?? throw new FareException("FARE_POLICY_NOT_FOUND", StatusCodes.Status404NotFound, "Fare policy version was not found.");
        if (version.Status is not ("DRAFT" or "PENDING_APPROVAL"))
            throw new FareException("INVALID_FARE_VERSION_TRANSITION", StatusCodes.Status409Conflict, $"Cannot approve fare policy version from status '{version.Status}'.");
        if (version.CreatedByUserId == adminUserId)
            throw new FareException("MAKER_CHECKER_VIOLATION", StatusCodes.Status409Conflict, "Creator cannot approve their own fare policy version.");
        if (version.MatrixEntries.Count == 0)
            throw new FareException("FARE_VERSION_EMPTY_MATRIX", StatusCodes.Status422UnprocessableEntity, "Fare policy version must have at least one matrix entry before approval.");

        var before = VersionSnapshot(version);
        var now = DateTime.UtcNow;
        version.Status = "APPROVED";
        version.ApprovedByUserId = adminUserId;
        version.ApprovedAt = now;
        version.DecisionReason = request.Reason?.Trim() ?? version.DecisionReason;
        version.UpdatedAt = now;
        AdminAuditHelper.AddAuditLog(db, opId, adminUserId, "APPROVE_FARE_POLICY_VERSION", "FARE_POLICY_VERSION", version.Id,
            ["status", "approved_by_user_id", "approved_at", "decision_reason"], before, VersionSnapshot(version), request.Reason);
        await SaveMappedAsync("FARE_VERSION_CONFLICT", "Fare policy version approval conflicted.", ct: ct);
        return ToVersionResponse(version);
    }

    public async Task<AdminFarePolicyVersionResponse> PublishVersionAsync(Guid versionId, FarePolicyDecisionRequest request, string adminUserId, CancellationToken ct)
    {
        var opId = await AdminAuditHelper.ResolveAdminOperatorAsync(db, operatorContext, adminUserId, ct);
        var version = await db.FarePolicyVersions.Include(v => v.MatrixEntries).SingleOrDefaultAsync(v => v.Id == versionId, ct)
            ?? throw new FareException("FARE_POLICY_NOT_FOUND", StatusCodes.Status404NotFound, "Fare policy version was not found.");
        if (version.Status != "APPROVED")
            throw new FareException("INVALID_FARE_VERSION_TRANSITION", StatusCodes.Status409Conflict, "Fare policy version must be APPROVED before publishing.");
        if (version.CreatedByUserId == adminUserId)
            throw new FareException("MAKER_CHECKER_VIOLATION", StatusCodes.Status409Conflict, "Creator cannot publish their own fare policy version.");

        var before = VersionSnapshot(version);
        var now = DateTime.UtcNow;
        version.Status = "PUBLISHED";
        version.PublishedByUserId = adminUserId;
        version.PublishedAt = now;
        version.DecisionReason = request.Reason?.Trim() ?? version.DecisionReason;
        version.UpdatedAt = now;
        AdminAuditHelper.AddAuditLog(db, opId, adminUserId, "PUBLISH_FARE_POLICY_VERSION", "FARE_POLICY_VERSION", version.Id,
            ["status", "published_by_user_id", "published_at", "decision_reason"], before, VersionSnapshot(version), request.Reason);
        await SaveMappedAsync("FARE_VERSION_CONFLICT", "Fare policy version publish conflicted.",
            "FARE_VERSION_WINDOW_OVERLAP", "Published fare policy version overlaps an existing published effective window for the same scope.", ct);
        return ToVersionResponse(version);
    }

    private async Task SaveMappedAsync(string dupCode, string dupMsg, string? exCode = null, string? exMsg = null, CancellationToken ct = default)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) { throw AdminAuditHelper.MapDbUpdateException(ex, dupCode, dupMsg, exCode, exMsg); }
    }

    internal static (DateTime FromUtc, DateTime? ToUtc) ValidateWindow(DateTime from, DateTime? to)
    {
        try
        {
            var win = new TimeWindow(from.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(from, DateTimeKind.Utc) : from,
                to.HasValue ? (to.Value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(to.Value, DateTimeKind.Utc) : to.Value) : null);
            return (win.StartUtc!.Value, win.EndExclusiveUtc);
        }
        catch (ArgumentException ex)
        {
            throw new FareException("INVALID_EFFECTIVE_WINDOW", StatusCodes.Status400BadRequest, ex.Message);
        }
    }

    internal static string RequireText(string? value, int maxLen, string code, string message)
    {
        var t = value?.Trim() ?? string.Empty;
        if (t.Length == 0 || t.Length > maxLen) throw new FareException(code, StatusCodes.Status400BadRequest, message);
        return t;
    }

    private static object VersionSnapshot(FarePolicyVersion v) => new
    {
        v.Id, v.Code, v.Version, v.ScopeKey, v.Status, v.Currency, v.EffectiveFrom, v.EffectiveTo,
        v.CreatedByUserId, v.ApprovedByUserId, v.PublishedByUserId, v.DecisionReason
    };

    private static AdminFareMatrixEntryResponse ToEntryResponse(FareMatrixEntry e) => new(
        e.Id, e.FarePolicyVersionId, e.TicketProductId, e.LineId, e.OriginStationId, e.DestinationStationId,
        e.PaymentChannel, e.EligibilityGroup, e.AmountMinor, e.Sellable);

    private static AdminFarePolicyVersionResponse ToVersionResponse(FarePolicyVersion v) => new(
        v.Id, v.Code, v.Version, v.ScopeKey, v.Status, v.Currency, v.EffectiveFrom, v.EffectiveTo,
        v.CreatedByUserId, v.ApprovedByUserId, v.PublishedByUserId, v.ApprovedAt, v.PublishedAt,
        v.DecisionReason, v.CreatedAt, v.UpdatedAt, v.MatrixEntries.Select(ToEntryResponse).ToArray());
}
