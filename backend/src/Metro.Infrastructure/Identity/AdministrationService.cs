using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Metro.Application.Abstractions.Auth;
using Metro.Domain.Entities.Identity;
using Metro.Infrastructure.Persistence.Context;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Metro.Infrastructure.Identity;

public sealed class AdministrationService(ApplicationDbContext db, UserManager<IdentityUser> users,
    RoleManager<IdentityRole> roles) : IAdministrationService
{
    private static readonly string[] RoleCodes = ["Passenger", "StationStaff", "Admin"];
    private static readonly string[] StatusCodes = ["PENDING", "ACTIVE", "LOCKED", "DISABLED", "CLOSED"];
    private static readonly string[] PassengerViewPurposes = ["SUPPORT_CASE", "ELIGIBILITY_REVIEW", "ACCOUNT_MAINTENANCE"];

    public async Task<AccountPage> FindAccountsAsync(string actorId, string? search, string? role, string? status,
        int page, int pageSize, CancellationToken ct)
    {
        if (page < 1 || pageSize is < 1 or > 100 || page > int.MaxValue / pageSize || search?.Length > 200 ||
            role is not null && !RoleCodes.Contains(role) || status is not null && !StatusCodes.Contains(status))
            throw Error("INVALID_ACCOUNT_FILTER", 400, "Invalid pagination, search, role or status.");
        var query = from user in db.Users.AsNoTracking()
                    join profile in db.UserProfiles.AsNoTracking() on user.Id equals profile.UserId into profiles
                    from profile in profiles.DefaultIfEmpty()
                    where user.EmailConfirmed
                    select new { user.Id, user.Email, FullName = profile == null ? "" : profile.FullName,
                        Status = profile == null ? "ACTIVE" : profile.Status };
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToUpperInvariant();
            query = query.Where(x => (x.Email != null && x.Email.ToUpper().Contains(term)) || x.FullName.ToUpper().Contains(term));
        }
        if (status is not null) query = query.Where(x => x.Status == status);
        if (role is not null)
            query = query.Where(x => (from assignment in db.UserRoles
                                     join r in db.Roles on assignment.RoleId equals r.Id
                                     where assignment.UserId == x.Id && r.Name == role select assignment).Any());
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.Email).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(ct);
        var ids = rows.Select(x => x.Id).ToArray();
        var assignments = await (from assignment in db.UserRoles.AsNoTracking()
                                 join r in db.Roles.AsNoTracking() on assignment.RoleId equals r.Id
                                 where ids.Contains(assignment.UserId)
                                 select new { assignment.UserId, r.Name }).ToArrayAsync(ct);
        db.AuditLogs.Add(Audit(actorId, "ACCOUNTS_SEARCHED", "AspNetUsers", "accounts", "ACCOUNT_MANAGEMENT"));
        await db.SaveChangesAsync(ct);
        return new AccountPage(rows.Select(x => new AccountSummary(x.Id, x.Email ?? "", x.FullName, x.Status,
            assignments.Where(a => a.UserId == x.Id).Select(a => a.Name!).Order().ToArray())).ToArray(), total, page, pageSize);
    }

    public Task ChangeRolesAsync(string actorId, string userId, AccountRolesRequest request, CancellationToken ct)
    {
        var reason = Reason(request.Reason);
        if (request.Roles is null || request.Roles.Length is < 1 or > 3 ||
            request.Roles.Any(x => !RoleCodes.Contains(x)) || request.Roles.Distinct().Count() != request.Roles.Length ||
            request.Roles.Contains("Admin") && request.Roles.Contains("StationStaff"))
            throw Error("INVALID_ROLES", 400, "Use Passenger and at most one supported staff role.");
        // Staff retain the right to use passenger features for themselves (BR-012).
        var desired = request.Roles.Append("Passenger").Distinct().Order().ToArray();
        return InTransactionAsync(actorId, userId, async () =>
        {
            var user = await UserAsync(userId, ct);
            if (!user.EmailConfirmed) throw Error("EMAIL_NOT_VERIFIED", 409, "Verify email before assigning roles.");
            var previous = (await users.GetRolesAsync(user)).Order().ToArray();
            if (previous.SequenceEqual(desired)) return;
            var added = desired.Except(previous).ToArray();
            if (added.Any(x => x is "Admin" or "StationStaff") &&
                !await db.UserProfiles.AnyAsync(x => x.UserId == userId && x.Status == "ACTIVE", ct))
                throw Error("ACCOUNT_NOT_ACTIVE", 409, "Activate the account before granting staff roles.");
            if (previous.Contains("Admin") && !desired.Contains("Admin")) await KeepAdministratorAsync(userId, ct);
            foreach (var role in desired)
                if (!await roles.RoleExistsAsync(role)) Check(await roles.CreateAsync(new IdentityRole(role)));
            var removed = previous.Except(desired).ToArray();
            if (removed.Length > 0) Check(await users.RemoveFromRolesAsync(user, removed));
            if (added.Length > 0) Check(await users.AddToRolesAsync(user, added));
            await RevokeSessionsAsync(user, "ROLES_CHANGED", ct);
            db.AuditLogs.Add(Audit(actorId, "ACCOUNT_ROLES_CHANGED", "AspNetUsers", userId, reason,
                ["roles"], Hash(previous), Hash(desired)));
            await NotifyAsync(userId, "ACCOUNT_ROLES_CHANGED", ct);
        }, ct);
    }

    public Task ChangeStatusAsync(string actorId, string userId, AccountStatusRequest request, CancellationToken ct)
    {
        var reason = Reason(request.Reason);
        if (request.Status is not ("ACTIVE" or "LOCKED"))
            throw Error("INVALID_ACCOUNT_STATUS", 400, "Only ACTIVE and LOCKED are supported.");
        return InTransactionAsync(actorId, userId, async () =>
        {
            var user = await UserAsync(userId, ct);
            var profile = await ProfileAsync(userId, ct);
            if (profile.Status == request.Status) return;
            if (profile.Status is "CLOSED" or "DISABLED")
                throw Error("INVALID_ACCOUNT_TRANSITION", 409, "This account cannot be locked or unlocked.");
            if (!user.EmailConfirmed) throw Error("EMAIL_NOT_VERIFIED", 409, "Verify email before activating or locking an account.");
            if (request.Status == "LOCKED" && await users.IsInRoleAsync(user, "Admin"))
                await KeepAdministratorAsync(userId, ct);
            var previous = profile.Status;
            profile.Status = request.Status;
            profile.UpdatedAt = DateTime.UtcNow;
            if (request.Status == "LOCKED") await RevokeSessionsAsync(user, "ACCOUNT_LOCKED", ct);
            db.AuditLogs.Add(Audit(actorId, "ACCOUNT_" + (request.Status == "LOCKED" ? "LOCKED" : "UNLOCKED"),
                "user_profiles", userId, reason, ["status"], Hash(previous), Hash(profile.Status)));
            await NotifyAsync(userId, "ACCOUNT_STATUS_CHANGED", ct);
        }, ct);
    }

    public async Task<PassengerDetails> GetPassengerAsync(string actorId, string userId, string? purpose, CancellationToken ct)
    {
        var purposeCode = purpose?.Trim();
        if (string.IsNullOrEmpty(purposeCode) || !PassengerViewPurposes.Contains(purposeCode))
        {
            var code = string.IsNullOrEmpty(purposeCode) ? "PURPOSE_REQUIRED" : "PURPOSE_INVALID";
            db.AuditLogs.Add(Audit(actorId, "PASSENGER_ACCESS_DENIED", "user_profiles", userId, code, outcome: "DENIED"));
            await db.SaveChangesAsync(ct);
            throw Error(code, 400, "Use purpose SUPPORT_CASE, ELIGIBILITY_REVIEW or ACCOUNT_MAINTENANCE.");
        }
        var user = await UserAsync(userId, ct);
        var profile = await ProfileAsync(userId, ct);
        var eligibility = await db.EligibilityEvidence.AsNoTracking().Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CreatedAt).Select(x => new PassengerEligibility(x.Id, x.Category, x.Status, x.ValidFrom, x.ValidTo)).ToArrayAsync(ct);
        // Save the access event before any PII can leave the service. No document keys or ciphertext are returned.
        db.AuditLogs.Add(Audit(actorId, "PASSENGER_VIEWED", "user_profiles", userId, purposeCode));
        await db.SaveChangesAsync(ct);
        return new PassengerDetails(userId, user.Email ?? "", profile.FullName, profile.BirthDate,
            Mask(user.PhoneNumber), profile.Status, MaskLast4(profile.CitizenIdLast4), MaskLast4(profile.BankAccountLast4),
            profile.BankCode, eligibility);
    }

    public Task UpdatePassengerAsync(string actorId, string userId, PassengerUpdateRequest request, CancellationToken ct)
    {
        var reason = Reason(request.Reason);
        var name = request.FullName?.Trim();
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        if (string.IsNullOrEmpty(name) || name.Length > 200 || request.BirthDate > today || request.BirthDate < today.AddYears(-130))
            throw Error("INVALID_PROFILE", 400, "Name or date of birth is invalid.");
        return InTransactionAsync(actorId, userId, async () =>
        {
            var profile = await ProfileAsync(userId, ct);
            var before = Hash(new { profile.FullName, profile.BirthDate, profile.CitizenIdVerifiedAt });
            var fields = new List<string>();
            if (profile.FullName != name)
            {
                profile.FullName = name;
                profile.NormalizedFullName = name.Normalize(NormalizationForm.FormKC).ToUpperInvariant();
                fields.Add("full_name");
            }
            if (profile.BirthDate != request.BirthDate) { profile.BirthDate = request.BirthDate; fields.Add("birth_date"); }
            if (fields.Count == 0) return;
            profile.UpdatedAt = DateTime.UtcNow;
            if (profile.CitizenIdVerifiedAt is not null)
            {
                profile.CitizenIdVerifiedAt = null;
                fields.Add("citizen_id_verified_at");
            }
            var approved = await db.EligibilityEvidence.Where(x => x.UserId == userId && x.Status == "VERIFIED").ToArrayAsync(ct);
            foreach (var item in approved)
            {
                var oldHash = Hash(new { item.Status, item.ValidFrom, item.ValidTo });
                item.Status = "NEEDS_INFO";
                item.DecisionReason = reason;
                item.DecidedAt = profile.UpdatedAt;
                item.DecidedByUserId = actorId;
                item.UpdatedAt = profile.UpdatedAt;
                db.Set<EligibilityDecision>().Add(new EligibilityDecision
                {
                    RequestId = item.Id, Status = "NEEDS_INFO", Reason = reason, ActorUserId = actorId,
                    OccurredAt = profile.UpdatedAt, ValidFrom = item.ValidFrom, ValidTo = item.ValidTo,
                });
                db.AuditLogs.Add(Audit(actorId, "ELIGIBILITY_REVIEW_REQUIRED", "profile_eligibility_requests", item.Id.ToString(),
                    reason, ["status"], oldHash, Hash(new { item.Status, item.ValidFrom, item.ValidTo })));
            }
            db.AuditLogs.Add(Audit(actorId, "PASSENGER_UPDATED", "user_profiles", userId, reason,
                fields.ToArray(), before, Hash(new { profile.FullName, profile.BirthDate, profile.CitizenIdVerifiedAt })));
            await NotifyAsync(userId, "PASSENGER_UPDATED", ct);
        }, ct);
    }

    private Task InTransactionAsync(string actorId, string userId, Func<Task> action, CancellationToken ct)
        => db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            // Serialize all admin changes, including two attempts to remove different administrators.
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(73190624011222)", ct);
            // Also serialize against login/refresh and the BE-04 profile/review/lifecycle operations.
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({userId}, 4))", ct);
            var actor = await UserAsync(actorId, ct);
            if (!actor.EmailConfirmed || !await users.IsInRoleAsync(actor, "Admin") ||
                await db.UserProfiles.AnyAsync(x => x.UserId == actorId && (x.Status == "LOCKED" || x.Status == "DISABLED" || x.Status == "CLOSED"), ct))
                throw Error("ADMIN_REQUIRED", 403, "An active administrator is required.");
            await action();
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        });

    private async Task KeepAdministratorAsync(string targetId, CancellationToken ct)
    {
        var another = await (from user in db.Users
                             join assignment in db.UserRoles on user.Id equals assignment.UserId
                             join role in db.Roles on assignment.RoleId equals role.Id
                             where role.Name == "Admin" && user.Id != targetId && user.EmailConfirmed
                             && !db.UserProfiles.Any(p => p.UserId == user.Id && (p.Status == "LOCKED" || p.Status == "DISABLED" || p.Status == "CLOSED"))
                             select user.Id).AnyAsync(ct);
        if (!another) throw Error("LAST_ACTIVE_ADMIN", 409, "At least one active administrator must remain.");
    }

    private async Task RevokeSessionsAsync(IdentityUser user, string reason, CancellationToken ct)
    {
        user.SecurityStamp = Guid.NewGuid().ToString();
        user.ConcurrencyStamp = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;
        foreach (var session in await db.AuthSessions.Where(x => x.UserId == user.Id && x.RevokedAt == null).ToArrayAsync(ct))
        { session.RevokedAt = now; session.RevocationReason = reason; }
    }

    private async Task NotifyAsync(string userId, string action, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new { action });
        foreach (var channel in new[] { "IN_APP", "EMAIL_MOCK" })
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO metro.notifications (recipient_user_id, notification_type, channel, template_code, content_reference, mandatory) VALUES ({userId}, 'ACCOUNT_CHANGE', {channel}, 'ACCOUNT_CHANGE', CAST({payload} AS jsonb), true)", ct);
    }

    private async Task<IdentityUser> UserAsync(string id, CancellationToken ct) =>
        await db.Users.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw Error("USER_NOT_FOUND", 404, "Account not found.");
    private async Task<UserProfile> ProfileAsync(string id, CancellationToken ct) =>
        await db.UserProfiles.SingleOrDefaultAsync(x => x.UserId == id, ct) ?? throw Error("PROFILE_NOT_FOUND", 404, "Profile not found.");
    private static string Reason(string? value) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= 1000
        ? value.Trim() : throw Error("REASON_REQUIRED", 400, "A reason of at most 1000 characters is required.");
    private static string? Mask(string? value) => string.IsNullOrEmpty(value) ? null : "****" + value[^Math.Min(4, value.Length)..];
    private static string? MaskLast4(string? value) => string.IsNullOrEmpty(value) ? null : "****" + value;
    private static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value)))).ToLowerInvariant();
    private static AuthException Error(string code, int status, string message) => new(code, status, message);
    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded) throw Error("ACCOUNT_CONFLICT", 409, "The account changed. Reload and try again.");
    }
    private static AuditLog Audit(string actor, string action, string type, string id, string reason,
        string[]? fields = null, string? before = null, string? after = null, string outcome = "SUCCEEDED") => new()
    {
        ActorUserId = actor, Action = action, EntityType = type, EntityId = id, Reason = reason,
        ChangedFields = fields ?? [], BeforeHash = before, AfterHash = after, Outcome = outcome,
    };
}
