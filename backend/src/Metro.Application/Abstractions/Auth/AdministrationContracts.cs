using System.Text.Json.Serialization;

namespace Metro.Application.Abstractions.Auth;

// Reject unrecognized fields so an attempted email/document update cannot silently succeed.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AccountRolesRequest(string[] Roles, string Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AccountStatusRequest(string Status, string Reason);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PassengerUpdateRequest(string FullName, DateOnly BirthDate, string Reason);
public sealed record AccountSummary(string UserId, string Email, string FullName, string Status, string[] Roles);
public sealed record AccountPage(AccountSummary[] Items, int Total, int Page, int PageSize);
public sealed record AdministrationRole(string Code, string Name, string[] Permissions);
public sealed record PassengerDetails(string UserId, string Email, string FullName, DateOnly BirthDate,
    string? PhoneNumber, string Status, string? CitizenId, string? BankAccount, string? BankCode,
    PassengerEligibility[] Eligibility);
public sealed record PassengerEligibility(Guid Id, string Category, string Status, DateOnly? ValidFrom, DateOnly? ValidTo);

public interface IAdministrationService
{
    Task<AccountPage> FindAccountsAsync(string actorId, string? search, string? role, string? status, int page, int pageSize, CancellationToken ct);
    Task ChangeRolesAsync(string actorId, string userId, AccountRolesRequest request, CancellationToken ct);
    Task ChangeStatusAsync(string actorId, string userId, AccountStatusRequest request, CancellationToken ct);
    Task<PassengerDetails> GetPassengerAsync(string actorId, string userId, string? purpose, CancellationToken ct);
    Task UpdatePassengerAsync(string actorId, string userId, PassengerUpdateRequest request, CancellationToken ct);
}
