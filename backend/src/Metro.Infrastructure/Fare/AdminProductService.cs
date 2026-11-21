using Metro.Application.Abstractions.Fare;
using Metro.Application.Abstractions.MultiTenancy;
using Metro.Application.Fare;
using Metro.Domain.Entities.Orders;
using Metro.Infrastructure.Persistence.Context;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Metro.Infrastructure.Fare;

public sealed class AdminProductService(ApplicationDbContext db, IOperatorContext operatorContext) : IAdminProductService
{
    public async Task<IReadOnlyList<AdminTicketProductResponse>> ListAsync(string? status, string adminUserId, CancellationToken ct)
    {
        await AdminAuditHelper.ResolveAdminOperatorAsync(db, operatorContext, adminUserId, ct);
        var query = db.TicketProducts.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalized = NormalizeProductStatus(status);
            query = query.Where(p => p.Status == normalized);
        }
        var items = await query.OrderBy(p => p.Code).ToListAsync(ct);
        return items.Select(ToResponse).ToArray();
    }

    public async Task<AdminTicketProductResponse> CreateAsync(CreateTicketProductRequest request, string adminUserId, CancellationToken ct)
    {
        var opId = await AdminAuditHelper.ResolveAdminOperatorAsync(db, operatorContext, adminUserId, ct);
        var code = NormalizeRequired(request.Code, 50, "PRODUCT_CODE_REQUIRED", "Product code is required (max 50 chars).");
        var name = NormalizeRequired(request.Name, 200, "PRODUCT_NAME_REQUIRED", "Product name is required.");
        var (type, durationVal, durationUnit, expiryRule) = FareRules.ValidateProductSpec(
            request.ProductType, request.DurationValue, request.DurationUnit, request.ExpiryRule, request.CapacityControlled);
        var status = NormalizeProductStatus(string.IsNullOrWhiteSpace(request.Status) ? "ACTIVE" : request.Status);
        var activation = ValidateLatestActivation(request.LatestActivationDays);
        var now = DateTime.UtcNow;

        var entity = new TicketProduct
        {
            OperatorId = opId, Code = code, Name = name, ProductType = type,
            DurationValue = durationVal, DurationUnit = durationUnit, ExpiryRule = expiryRule,
            LatestActivationInterval = activation, RequiresNamedHolder = request.RequiresNamedHolder,
            CapacityControlled = false, Status = status,
            CreatedByUserId = adminUserId, UpdatedByUserId = adminUserId, CreatedAt = now, UpdatedAt = now,
        };
        db.TicketProducts.Add(entity);
        AdminAuditHelper.AddAuditLog(db, opId, adminUserId, "CREATE_TICKET_PRODUCT", "TICKET_PRODUCT", entity.Id,
            ["code", "name", "product_type", "duration_value", "duration_unit", "expiry_rule", "status"],
            before: null, after: Snapshot(entity), reason: request.Reason);

        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex)
        {
            throw AdminAuditHelper.MapDbUpdateException(ex, "PRODUCT_CODE_EXISTS", $"Ticket product code '{code}' already exists.");
        }
        return ToResponse(entity);
    }

    public async Task<AdminTicketProductResponse> UpdateAsync(Guid productId, UpdateTicketProductRequest request, string adminUserId, CancellationToken ct)
    {
        var opId = await AdminAuditHelper.ResolveAdminOperatorAsync(db, operatorContext, adminUserId, ct);
        var entity = await db.TicketProducts.SingleOrDefaultAsync(p => p.Id == productId, ct)
            ?? throw new FareException("PRODUCT_NOT_FOUND", StatusCodes.Status404NotFound, "Ticket product was not found.");

        var before = Snapshot(entity);
        var name = NormalizeRequired(request.Name, 200, "PRODUCT_NAME_REQUIRED", "Product name is required.");
        var (_, durationVal, durationUnit, expiryRule) = FareRules.ValidateProductSpec(
            entity.ProductType, request.DurationValue, request.DurationUnit, request.ExpiryRule, entity.CapacityControlled);
        var status = NormalizeProductStatus(request.Status);
        var activation = ValidateLatestActivation(request.LatestActivationDays);

        var changed = new List<string>();
        if (entity.Name != name) { entity.Name = name; changed.Add("name"); }
        if (entity.DurationValue != durationVal) { entity.DurationValue = durationVal; changed.Add("duration_value"); }
        if (entity.DurationUnit != durationUnit) { entity.DurationUnit = durationUnit; changed.Add("duration_unit"); }
        if (entity.ExpiryRule != expiryRule) { entity.ExpiryRule = expiryRule; changed.Add("expiry_rule"); }
        if (entity.LatestActivationInterval != activation) { entity.LatestActivationInterval = activation; changed.Add("latest_activation_interval"); }
        if (entity.RequiresNamedHolder != request.RequiresNamedHolder) { entity.RequiresNamedHolder = request.RequiresNamedHolder; changed.Add("requires_named_holder"); }
        if (entity.Status != status) { entity.Status = status; changed.Add("status"); }

        entity.UpdatedByUserId = adminUserId;
        entity.UpdatedAt = DateTime.UtcNow;
        AdminAuditHelper.AddAuditLog(db, opId, adminUserId, "UPDATE_TICKET_PRODUCT", "TICKET_PRODUCT", entity.Id,
            changed.Count > 0 ? [.. changed] : ["updated_at"], before, Snapshot(entity), request.Reason);

        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex)
        {
            throw AdminAuditHelper.MapDbUpdateException(ex, "PRODUCT_CONFLICT", "Ticket product update conflicted with existing data.");
        }
        return ToResponse(entity);
    }

    private static string NormalizeRequired(string? value, int maxLen, string code, string message)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed.Length > maxLen)
            throw new FareException(code, StatusCodes.Status400BadRequest, message);
        return trimmed;
    }

    private static string NormalizeProductStatus(string? status)
    {
        var normalized = (status ?? string.Empty).Trim().ToUpperInvariant();
        if (!FareRules.SupportedProductStatuses.Contains(normalized))
            throw new FareException("INVALID_PRODUCT_STATUS", StatusCodes.Status400BadRequest,
                "Product status must be one of: DRAFT, ACTIVE, SUSPENDED, RETIRED.");
        return normalized;
    }

    private static TimeSpan? ValidateLatestActivation(int? days)
    {
        if (days is null) return null;
        if (days <= 0 || days > 365)
            throw new FareException("INVALID_ACTIVATION_WINDOW", StatusCodes.Status400BadRequest,
                "latestActivationDays must be between 1 and 365.");
        return TimeSpan.FromDays(days.Value);
    }

    private static object Snapshot(TicketProduct p) => new
    {
        p.Id, p.Code, p.Name, p.ProductType, p.DurationValue, p.DurationUnit,
        p.ExpiryRule, p.LatestActivationInterval, p.RequiresNamedHolder, p.CapacityControlled, p.Status
    };

    private static AdminTicketProductResponse ToResponse(TicketProduct p) => new(
        p.Id, p.Code, p.Name, p.ProductType, p.DurationValue, p.DurationUnit, p.ExpiryRule,
        p.LatestActivationInterval, p.RequiresNamedHolder, p.CapacityControlled, p.Status,
        p.CreatedByUserId, p.UpdatedByUserId, p.CreatedAt, p.UpdatedAt);
}
