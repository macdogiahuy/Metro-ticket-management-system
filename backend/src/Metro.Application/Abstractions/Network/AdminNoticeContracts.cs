namespace Metro.Application.Abstractions.Network;

/// <summary>BE-13 UC31 admin service notice contracts under /api/v1/admin/notices.</summary>
public sealed record CreateServiceNoticeRequest(
    string Code, Guid? IncidentId, string Audience, string Title, string Content,
    bool EmergencyTemplate, DateTime ValidFrom, DateTime? ValidTo, string? Reason);

public sealed record UpdateServiceNoticeRequest(
    Guid? IncidentId, string Audience, string Title, string Content,
    bool EmergencyTemplate, DateTime ValidFrom, DateTime? ValidTo, string? Reason);

public sealed record ServiceNoticeDecisionRequest(string? Reason, bool Expire = false);

public sealed record AdminServiceNoticeResponse(
    Guid Id, string Code, Guid? IncidentId, string Audience, string Title, string Content,
    string Status, bool EmergencyTemplate, DateTime ValidFrom, DateTime? ValidTo, bool IsCurrentlyEffective,
    string CreatedByUserId, string? ApprovedByUserId, string? PublishedByUserId,
    DateTime? ApprovedAt, DateTime? PublishedAt, string? DecisionReason, DateTime CreatedAt, DateTime UpdatedAt);

public interface IAdminNoticeService
{
    Task<IReadOnlyList<AdminServiceNoticeResponse>> ListAsync(string? status, string? audience, bool activeOnly, string adminUserId, CancellationToken ct);
    Task<AdminServiceNoticeResponse> GetAsync(Guid noticeId, string adminUserId, CancellationToken ct);
    Task<AdminServiceNoticeResponse> CreateAsync(CreateServiceNoticeRequest request, string adminUserId, CancellationToken ct);
    Task<AdminServiceNoticeResponse> UpdateAsync(Guid noticeId, UpdateServiceNoticeRequest request, string adminUserId, CancellationToken ct);
    Task<AdminServiceNoticeResponse> ApproveAsync(Guid noticeId, ServiceNoticeDecisionRequest request, string adminUserId, CancellationToken ct);
    Task<AdminServiceNoticeResponse> PublishAsync(Guid noticeId, ServiceNoticeDecisionRequest request, string adminUserId, CancellationToken ct);
    Task<AdminServiceNoticeResponse> CancelOrExpireAsync(Guid noticeId, ServiceNoticeDecisionRequest request, string adminUserId, CancellationToken ct);
}
