namespace Metro.Application.Abstractions.Fare;

public sealed record CreateEligibilityPolicyRequest(
    string Code,
    string BeneficiaryGroup,
    string DiscountType,
    decimal DiscountValue,
    IReadOnlyList<string>? EligibleProductCodes,
    IReadOnlyList<string>? RequiredEvidenceTypes,
    DateTime EffectiveFrom,
    DateTime? EffectiveTo,
    string? Reason);

public sealed record AdminEligibilityPolicyResponse(
    Guid Id,
    string Code,
    int Version,
    string BeneficiaryGroup,
    string DiscountType,
    decimal DiscountValue,
    IReadOnlyList<string> EligibleProductCodes,
    IReadOnlyList<string> RequiredEvidenceTypes,
    DateTime EffectiveFrom,
    DateTime? EffectiveTo,
    string Status,
    string CreatedByUserId,
    string? ApprovedByUserId,
    DateTime? ApprovedAt,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public interface IAdminEligibilityPolicyService
{
    Task<IReadOnlyList<AdminEligibilityPolicyResponse>> ListAsync(string? status, string adminUserId, CancellationToken ct);
    Task<AdminEligibilityPolicyResponse> CreateAsync(CreateEligibilityPolicyRequest request, string adminUserId, CancellationToken ct);
    Task<AdminEligibilityPolicyResponse> ApproveAsync(Guid policyId, FarePolicyDecisionRequest request, string adminUserId, CancellationToken ct);
    Task<AdminEligibilityPolicyResponse> PublishAsync(Guid policyId, FarePolicyDecisionRequest request, string adminUserId, CancellationToken ct);
}
