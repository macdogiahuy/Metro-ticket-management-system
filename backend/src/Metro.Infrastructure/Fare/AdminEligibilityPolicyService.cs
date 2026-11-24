using Metro.Application.Abstractions.Fare;
using Metro.Application.Abstractions.MultiTenancy;
using Metro.Application.Fare;
using Metro.Domain.Entities.Orders;
using Metro.Infrastructure.Persistence.Context;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Metro.Infrastructure.Fare;

public sealed class AdminEligibilityPolicyService(ApplicationDbContext db, IOperatorContext operatorContext) : IAdminEligibilityPolicyService
{
    public async Task<IReadOnlyList<AdminEligibilityPolicyResponse>> ListAsync(string? status, string adminUserId, CancellationToken ct)
    {
        await AdminAuditHelper.ResolveAdminOperatorAsync(db, operatorContext, adminUserId, ct);
        var query = db.EligibilityPolicies.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(p => p.Status == status.Trim().ToUpperInvariant());
        var list = await query.OrderBy(p => p.Code).ThenByDescending(p => p.Version).ToListAsync(ct);
        return list.Select(ToResponse).ToArray();
    }

    public async Task<AdminEligibilityPolicyResponse> CreateAsync(CreateEligibilityPolicyRequest request, string adminUserId, CancellationToken ct)
    {
        var opId = await AdminAuditHelper.ResolveAdminOperatorAsync(db, operatorContext, adminUserId, ct);
        var code = AdminFareService.RequireText(request.Code, 50, "ELIGIBILITY_POLICY_CODE_REQUIRED", "Eligibility policy code is required (max 50 chars).");
        var group = FareRules.NormalizeEligibilityGroup(request.BeneficiaryGroup);
        if (group == "ORDINARY")
            throw new FareException("UNSUPPORTED_ELIGIBILITY_GROUP", StatusCodes.Status400BadRequest, "Eligibility policies apply only to STUDENT, ELDERLY, or CHILD_UNDER_6 (FR-25.01).");
        var discountType = (request.DiscountType ?? string.Empty).Trim().ToUpperInvariant();
        if (discountType is not ("FREE" or "PERCENT" or "FIXED_AMOUNT"))
            throw new FareException("INVALID_DISCOUNT_TYPE", StatusCodes.Status400BadRequest, "discountType must be FREE, PERCENT, or FIXED_AMOUNT.");
        if (request.DiscountValue < 0 || (discountType == "PERCENT" && request.DiscountValue > 100))
            throw new FareException("INVALID_DISCOUNT_VALUE", StatusCodes.Status400BadRequest, "discountValue must be >= 0 (and <= 100 for PERCENT).");

        var productCodes = (request.EligibleProductCodes ?? []).Select(c => c.Trim()).Where(c => c.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (group == "CHILD_UNDER_6")
        {
            if (discountType != "FREE")
                throw new FareException("CHILD_MUST_BE_FREE", StatusCodes.Status400BadRequest, "CHILD_UNDER_6 policy must use discountType = FREE (BR-021).");
            if (productCodes.Length > 0 && await db.TicketProducts.AsNoTracking().AnyAsync(p => productCodes.Contains(p.Code) && p.ProductType != "SINGLE_TRIP", ct))
                throw new FareException("CHILD_SINGLE_TRIP_ONLY", StatusCodes.Status400BadRequest, "CHILD_UNDER_6 eligibility can only include SINGLE_TRIP products (BR-021).");
        }

        var (fromUtc, toUtc) = AdminFareService.ValidateWindow(request.EffectiveFrom, request.EffectiveTo);
        var nextVersion = (await db.EligibilityPolicies.AsNoTracking().Where(p => p.Code == code).MaxAsync(p => (int?)p.Version, ct) ?? 0) + 1;
        var now = DateTime.UtcNow;
        var entity = new EligibilityPolicy
        {
            OperatorId = opId, Code = code, Version = nextVersion, BeneficiaryGroup = group,
            DiscountType = discountType, DiscountValue = request.DiscountValue, EligibleProductCodes = productCodes,
            RequiredEvidenceTypes = (request.RequiredEvidenceTypes ?? []).Select(e => e.Trim()).Where(e => e.Length > 0).Distinct().ToArray(),
            EffectiveFrom = fromUtc, EffectiveTo = toUtc, Status = "DRAFT", CreatedByUserId = adminUserId, CreatedAt = now, UpdatedAt = now,
        };
        db.EligibilityPolicies.Add(entity);
        AdminAuditHelper.AddAuditLog(db, opId, adminUserId, "CREATE_ELIGIBILITY_POLICY", "ELIGIBILITY_POLICY", entity.Id,
            ["code", "version", "beneficiary_group", "discount_type", "discount_value", "status"], null, ToResponse(entity), request.Reason);
        await SaveMappedAsync("ELIGIBILITY_POLICY_CONFLICT", $"Eligibility policy '{code}' version conflict.", ct);
        return ToResponse(entity);
    }

    public async Task<AdminEligibilityPolicyResponse> ApproveAsync(Guid policyId, FarePolicyDecisionRequest request, string adminUserId, CancellationToken ct)
    {
        var opId = await AdminAuditHelper.ResolveAdminOperatorAsync(db, operatorContext, adminUserId, ct);
        var policy = await db.EligibilityPolicies.SingleOrDefaultAsync(p => p.Id == policyId, ct)
            ?? throw new FareException("ELIGIBILITY_POLICY_NOT_FOUND", StatusCodes.Status404NotFound, "Eligibility policy was not found.");
        if (policy.Status is not ("DRAFT" or "PENDING_APPROVAL"))
            throw new FareException("INVALID_ELIGIBILITY_POLICY_TRANSITION", StatusCodes.Status409Conflict, $"Cannot approve eligibility policy from status '{policy.Status}'.");
        if (policy.CreatedByUserId == adminUserId)
            throw new FareException("MAKER_CHECKER_VIOLATION", StatusCodes.Status409Conflict, "Creator cannot approve their own eligibility policy.");

        var before = ToResponse(policy);
        var now = DateTime.UtcNow;
        policy.Status = "APPROVED";
        policy.ApprovedByUserId = adminUserId;
        policy.ApprovedAt = now;
        policy.UpdatedAt = now;
        AdminAuditHelper.AddAuditLog(db, opId, adminUserId, "APPROVE_ELIGIBILITY_POLICY", "ELIGIBILITY_POLICY", policy.Id,
            ["status", "approved_by_user_id", "approved_at"], before, ToResponse(policy), request.Reason);
        await SaveMappedAsync("ELIGIBILITY_POLICY_CONFLICT", "Eligibility policy approval conflicted.", ct);
        return ToResponse(policy);
    }

    public async Task<AdminEligibilityPolicyResponse> PublishAsync(Guid policyId, FarePolicyDecisionRequest request, string adminUserId, CancellationToken ct)
    {
        var opId = await AdminAuditHelper.ResolveAdminOperatorAsync(db, operatorContext, adminUserId, ct);
        var policy = await db.EligibilityPolicies.SingleOrDefaultAsync(p => p.Id == policyId, ct)
            ?? throw new FareException("ELIGIBILITY_POLICY_NOT_FOUND", StatusCodes.Status404NotFound, "Eligibility policy was not found.");
        if (policy.Status != "APPROVED")
            throw new FareException("INVALID_ELIGIBILITY_POLICY_TRANSITION", StatusCodes.Status409Conflict, "Eligibility policy must be APPROVED before publishing.");

        var before = ToResponse(policy);
        policy.Status = "PUBLISHED";
        policy.UpdatedAt = DateTime.UtcNow;
        AdminAuditHelper.AddAuditLog(db, opId, adminUserId, "PUBLISH_ELIGIBILITY_POLICY", "ELIGIBILITY_POLICY", policy.Id,
            ["status"], before, ToResponse(policy), request.Reason);
        await SaveMappedAsync("ELIGIBILITY_POLICY_CONFLICT", "Eligibility policy publish conflicted.", ct);
        return ToResponse(policy);
    }

    private async Task SaveMappedAsync(string dupCode, string dupMsg, CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) { throw AdminAuditHelper.MapDbUpdateException(ex, dupCode, dupMsg); }
    }

    private static AdminEligibilityPolicyResponse ToResponse(EligibilityPolicy p) => new(
        p.Id, p.Code, p.Version, p.BeneficiaryGroup, p.DiscountType, p.DiscountValue,
        p.EligibleProductCodes, p.RequiredEvidenceTypes, p.EffectiveFrom, p.EffectiveTo, p.Status,
        p.CreatedByUserId, p.ApprovedByUserId, p.ApprovedAt, p.CreatedAt, p.UpdatedAt);
}
