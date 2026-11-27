using System.Security.Cryptography;
using System.Text.Json;
using Metro.Application.Abstractions.MultiTenancy;
using Metro.Application.Abstractions.Network;
using Metro.Domain.Entities.Identity;
using Metro.Infrastructure.Persistence.Context;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Metro.Infrastructure.Network;

internal static class NetworkAdminHelper
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
            ?? throw new NetworkAdminException("OPERATOR_FORBIDDEN", StatusCodes.Status403Forbidden,
                "The admin account has no active operator membership.");
        operatorContext.CurrentOperatorId = opId;
        return opId;
    }

    public static string? ComputeHash(object? snapshot) =>
        snapshot is null ? null : Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(snapshot, CanonicalJson))).ToLowerInvariant();

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

    public static Exception MapPostgresException(
        Exception ex, string duplicateCode, string duplicateMessage,
        string? exclusionCode = null, string? exclusionMessage = null,
        string? checkCode = null, string? checkMessage = null)
    {
        var pg = ex is DbUpdateException dbEx ? dbEx.InnerException as PostgresException : ex as PostgresException;
        if (pg is null) return ex;
        return pg.SqlState switch
        {
            PostgresErrorCodes.UniqueViolation => new NetworkAdminException(duplicateCode, StatusCodes.Status409Conflict, duplicateMessage),
            PostgresErrorCodes.ExclusionViolation => new NetworkAdminException(
                exclusionCode ?? "OVERLAPPING_WINDOW", StatusCodes.Status409Conflict,
                exclusionMessage ?? "The requested time window overlaps an existing active record."),
            PostgresErrorCodes.CheckViolation => new NetworkAdminException(
                checkCode ?? "CONSTRAINT_VIOLATION", StatusCodes.Status400BadRequest,
                checkMessage ?? "The request violates a domain integrity constraint."),
            PostgresErrorCodes.ForeignKeyViolation => new NetworkAdminException("REFERENCE_NOT_FOUND", StatusCodes.Status404NotFound,
                "A referenced entity was not found in the current operator scope."),
            PostgresErrorCodes.NoDataFound => new NetworkAdminException("RESOURCE_NOT_FOUND", StatusCodes.Status404NotFound,
                "The requested resource was not found."),
            PostgresErrorCodes.RaiseException => new NetworkAdminException("STATE_CONFLICT", StatusCodes.Status409Conflict,
                "The requested operation conflicts with the current resource state."),
            _ => ex,
        };
    }
}
