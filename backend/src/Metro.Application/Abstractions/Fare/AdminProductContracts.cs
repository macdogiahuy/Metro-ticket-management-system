namespace Metro.Application.Abstractions.Fare;

public sealed record CreateTicketProductRequest(
    string Code,
    string Name,
    string ProductType,
    int? DurationValue,
    string? DurationUnit,
    string ExpiryRule,
    int? LatestActivationDays,
    bool RequiresNamedHolder,
    bool CapacityControlled,
    string? Status,
    string? Reason);

public sealed record UpdateTicketProductRequest(
    string Name,
    int? DurationValue,
    string? DurationUnit,
    string ExpiryRule,
    int? LatestActivationDays,
    bool RequiresNamedHolder,
    string Status,
    string? Reason);

public sealed record AdminTicketProductResponse(
    Guid Id,
    string Code,
    string Name,
    string ProductType,
    int? DurationValue,
    string? DurationUnit,
    string ExpiryRule,
    TimeSpan? LatestActivationInterval,
    bool RequiresNamedHolder,
    bool CapacityControlled,
    string Status,
    string? CreatedByUserId,
    string? UpdatedByUserId,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public interface IAdminProductService
{
    Task<IReadOnlyList<AdminTicketProductResponse>> ListAsync(string? status, string adminUserId, CancellationToken cancellationToken);
    Task<AdminTicketProductResponse> CreateAsync(CreateTicketProductRequest request, string adminUserId, CancellationToken cancellationToken);
    Task<AdminTicketProductResponse> UpdateAsync(Guid productId, UpdateTicketProductRequest request, string adminUserId, CancellationToken cancellationToken);
}
