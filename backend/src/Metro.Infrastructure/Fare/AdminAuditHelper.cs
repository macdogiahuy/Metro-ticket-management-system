using System.Security.Cryptography;
using System.Text.Json;
using Metro.Application.Abstractions.Fare;
using Metro.Application.Abstractions.MultiTenancy;
using Metro.Domain.Entities.Identity;
using Metro.Infrastructure.Persistence.Context;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Metro.Infrastructure.Fare;

internal static class AdminAuditHelper
{
    private static readonly JsonSerializerOptions CanonicalJson = new(JsonSerializerDefaults.Web);

    public static async Task<Guid> ResolveAdminOperatorAsync(
        ApplicationDbContext db, IOperatorContext operatorContext, string adminUserId, CancellationToken ct)
    {
        var opId = await db.OperatorAccounts.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.UserId == adminUserId && a.Status == "ACTIVE" && a.Operator.Status == "ACTIVE")
            .OrderBy(a => a.Id)
            .Select(a => (Guid?)a.OperatorId)
            .FirstOrDefaultAsync(ct)
            ?? throw new FareException("OPERATOR_NOT_FOUND", StatusCodes.Status403Forbidden,
                "The admin account has no active operator membership.");
        operatorContext.CurrentOperatorId = opId;
        return opId;
    }

    public static string? ComputeHash(object? snapshot)
    {
        if (snapshot is null) return null;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, CanonicalJson);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    public static void AddAuditLog(
        ApplicationDbContext db, Guid operatorId, string actorUserId, string action, string entityType,
        Guid entityId, string[] changedFields, object? before, object? after, string? reason)
    {
        db.AuditLogs.Add(new AuditLog
        {
            OperatorId = operatorId,
            ActorUserId = actorUserId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId.ToString(),
            ChangedFields = changedFields,
            BeforeHash = ComputeHash(before),
            AfterHash = ComputeHash(after),
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            Outcome = "SUCCEEDED",
            OccurredAt = DateTime.UtcNow,
        });
    }

    public static Exception MapDbUpdateException(
        DbUpdateException ex, string duplicateCode, string duplicateMessage,
        string? exclusionCode = null, string? exclusionMessage = null)
    {
        if (ex.InnerException is not PostgresException pg) return ex;
        return pg.SqlState switch
        {
            PostgresErrorCodes.UniqueViolation => new FareException(duplicateCode, StatusCodes.Status409Conflict, duplicateMessage),
            PostgresErrorCodes.ExclusionViolation => new FareException(
                exclusionCode ?? "OVERLAPPING_EFFECTIVE_WINDOW", StatusCodes.Status409Conflict,
                exclusionMessage ?? "Effective window overlaps an existing published version."),
            PostgresErrorCodes.CheckViolation => new FareException("DATABASE_CHECK_VIOLATION", StatusCodes.Status400BadRequest,
                "The requested change violates a domain integrity constraint."),
            PostgresErrorCodes.ForeignKeyViolation => new FareException("REFERENCE_NOT_FOUND", StatusCodes.Status404NotFound,
                "A referenced entity was not found in the current operator scope."),
            _ => ex,
        };
    }
}
