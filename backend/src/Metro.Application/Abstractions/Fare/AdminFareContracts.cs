namespace Metro.Application.Abstractions.Fare;

public sealed record CreateFarePolicyVersionRequest(
    string Code,
    string ScopeKey,
    DateTime EffectiveFrom,
    DateTime? EffectiveTo,
    string? Reason);

public sealed record FarePolicyDecisionRequest(string? Reason);

public sealed record UpsertFareMatrixEntryRequest(
    Guid TicketProductId,
    Guid? LineId,
    Guid? OriginStationId,
    Guid? DestinationStationId,
    string? PaymentChannel,
    string? EligibilityGroup,
    long AmountMinor,
    bool Sellable,
    string? Reason);

public sealed record AdminFareMatrixEntryResponse(
    Guid Id,
    Guid FarePolicyVersionId,
    Guid TicketProductId,
    Guid? LineId,
    Guid? OriginStationId,
    Guid? DestinationStationId,
    string PaymentChannel,
    string EligibilityGroup,
    long AmountMinor,
    bool Sellable);

public sealed record AdminFarePolicyVersionResponse(
    Guid Id,
    string Code,
    int Version,
    string ScopeKey,
    string Status,
    string Currency,
    DateTime EffectiveFrom,
    DateTime? EffectiveTo,
    string CreatedByUserId,
    string? ApprovedByUserId,
    string? PublishedByUserId,
    DateTime? ApprovedAt,
    DateTime? PublishedAt,
    string? DecisionReason,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<AdminFareMatrixEntryResponse> Entries);

public interface IAdminFareService
{
    Task<IReadOnlyList<AdminFarePolicyVersionResponse>> ListVersionsAsync(string? status, string? scopeKey, string adminUserId, CancellationToken ct);
    Task<AdminFarePolicyVersionResponse> GetVersionAsync(Guid versionId, string adminUserId, CancellationToken ct);
    Task<AdminFarePolicyVersionResponse> CreateVersionAsync(CreateFarePolicyVersionRequest request, string adminUserId, CancellationToken ct);
    Task<AdminFareMatrixEntryResponse> AddMatrixEntryAsync(Guid versionId, UpsertFareMatrixEntryRequest request, string adminUserId, CancellationToken ct);
    Task DeleteMatrixEntryAsync(Guid versionId, Guid entryId, string? reason, string adminUserId, CancellationToken ct);
    Task<AdminFarePolicyVersionResponse> ApproveVersionAsync(Guid versionId, FarePolicyDecisionRequest request, string adminUserId, CancellationToken ct);
    Task<AdminFarePolicyVersionResponse> PublishVersionAsync(Guid versionId, FarePolicyDecisionRequest request, string adminUserId, CancellationToken ct);
}
