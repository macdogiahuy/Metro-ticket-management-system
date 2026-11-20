using Metro.Application.Abstractions.Storage;
using Metro.Domain.Entities.Identity;
using Metro.Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Metro.Infrastructure.Identity;

/// <summary>Expires profile verification and removes private evidence after its retention period.</summary>
public sealed class ProfileEligibilityLifecycle(
    ApplicationDbContext db,
    IObjectStorage storage,
    ILogger<ProfileEligibilityLifecycle> logger)
{
    public async Task RunOnceAsync(DateTime utcNow, CancellationToken cancellationToken = default)
    {
        utcNow = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
        var today = DateOnly.FromDateTime(utcNow.AddHours(7));
        var cutoff = utcNow.AddDays(-30);
        var candidates = await db.EligibilityEvidence.AsNoTracking()
            .Where(x => (x.Status == "VERIFIED" && x.ValidTo < today)
                || (!x.EvidenceHold && (x.Status == "REJECTED" || x.Status == "EXPIRED" || x.Status == "REVOKED")
                    && (x.EvidenceObjectKey != null || x.Attachments.Any(a => a.ObjectKey != null))))
            .Select(x => new { x.Id, x.UserId }).ToListAsync(cancellationToken);

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                db.ChangeTracker.Clear();
                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
                // Share the per-passenger lock with profile/decision/upload endpoints.
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT pg_advisory_xact_lock(hashtextextended({candidate.UserId}, 4))", cancellationToken);
                var item = await db.EligibilityEvidence.Include(x => x.Attachments)
                    .SingleAsync(x => x.Id == candidate.Id, cancellationToken);
                if (item.Status == "VERIFIED" && item.ValidTo < today)
                {
                    item.Status = "EXPIRED";
                    item.UpdatedAt = utcNow;
                    db.Set<EligibilityDecision>().Add(new EligibilityDecision
                    {
                        RequestId = item.Id, Status = "EXPIRED", Reason = "Verification validity ended.",
                        OccurredAt = utcNow, ValidFrom = item.ValidFrom, ValidTo = item.ValidTo,
                    });
                    db.AuditLogs.Add(SystemAudit(item.Id, "ELIGIBILITY_EXPIRED", utcNow, ["status"]));
                }

                DateTime? retentionStart = item.Status == "EXPIRED" && item.ValidTo.HasValue
                    ? item.ValidTo.Value.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddHours(-7)
                    : item.DecidedAt;
                if (!item.EvidenceHold && (item.Status is "REJECTED" or "EXPIRED" or "REVOKED")
                    && retentionStart <= cutoff)
                {
                    var keys = item.Attachments.Where(x => x.ObjectKey != null).Select(x => x.ObjectKey!)
                        .Concat(item.EvidenceObjectKey is null ? [] : new[] { item.EvidenceObjectKey })
                        .Distinct(StringComparer.Ordinal).ToArray();
                    if (keys.Length > 0)
                    {
                        // Persist cleanup with the detached references. Storage is touched only
                        // after this transaction commits; the queue survives crashes and retries.
                        await db.Database.ExecuteSqlInterpolatedAsync($"""
                            INSERT INTO metro.profile_eligibility_purge_queue (request_id, object_keys, queued_at)
                            VALUES ({item.Id}, {keys}, {utcNow})
                            """, cancellationToken);
                        foreach (var attachment in item.Attachments.Where(x => x.ObjectKey != null))
                        {
                            attachment.ObjectKey = null;
                            attachment.PurgedAt = utcNow;
                        }
                        item.EvidenceObjectKey = null;
                        item.EvidenceFileName = null;
                        item.EvidenceContentType = null;
                        item.EvidenceSize = null;
                        item.EvidenceSha256 = null;
                        item.UpdatedAt = utcNow;
                        db.AuditLogs.Add(SystemAudit(item.Id, "ELIGIBILITY_EVIDENCE_PURGE_QUEUED", utcNow,
                            ["evidence_object_key", "attachments.purged_at"]));
                    }
                }
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            });
        }

        await PurgeQueuedEvidenceAsync(utcNow, cancellationToken);
    }

    private async Task PurgeQueuedEvidenceAsync(DateTime utcNow, CancellationToken cancellationToken)
    {
        var requestIds = await db.Database.SqlQuery<Guid>($"""
            SELECT request_id AS "Value" FROM metro.profile_eligibility_purge_queue ORDER BY queued_at, request_id
            """).ToListAsync(cancellationToken);
        foreach (var requestId in requestIds)
        {
            await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                db.ChangeTracker.Clear();
                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
                var jobs = await db.Database.SqlQuery<PendingPurge>($"""
                    SELECT object_keys AS "ObjectKeys" FROM metro.profile_eligibility_purge_queue
                    WHERE request_id = {requestId} FOR UPDATE SKIP LOCKED
                    """).ToListAsync(cancellationToken);
                if (jobs.Count == 0) return;
                try
                {
                    // A committed queue row guarantees no live DB reference points at these
                    // objects. Delete is idempotent if a previous attempt only partly completed.
                    foreach (var key in jobs[0].ObjectKeys)
                        await storage.DeleteAsync(key, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception exception)
                {
                    // Do not log object keys, provider messages, or passenger details.
                    logger.LogWarning("Profile eligibility evidence purge will retry after {ErrorType}.", exception.GetType().Name);
                    return;
                }

                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    DELETE FROM metro.profile_eligibility_purge_queue WHERE request_id = {requestId}
                    """, cancellationToken);
                db.AuditLogs.Add(SystemAudit(requestId, "ELIGIBILITY_EVIDENCE_PURGED", utcNow, ["object_storage"]));
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            });
        }
    }

    private sealed class PendingPurge
    {
        public string[] ObjectKeys { get; set; } = [];
    }

    private static AuditLog SystemAudit(Guid id, string action, DateTime utcNow, string[] fields) => new()
    {
        DatabaseActor = "profile-eligibility-lifecycle", Action = action,
        EntityType = "profile_eligibility_requests", EntityId = id.ToString(),
        ChangedFields = fields, Reason = "Scheduled verification lifecycle policy.", OccurredAt = utcNow,
    };
}

internal sealed class ProfileEligibilityLifecycleWorker(
    IServiceScopeFactory scopes,
    IHostEnvironment environment,
    ILogger<ProfileEligibilityLifecycleWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (environment.IsEnvironment("Testing")) return;
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<ProfileEligibilityLifecycle>()
                        .RunOnceAsync(DateTime.UtcNow, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception exception)
                {
                    // Do not log provider messages that may contain private connection/object details.
                    logger.LogWarning("Profile eligibility lifecycle will retry after {ErrorType}.", exception.GetType().Name);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}

public static class ProfileEligibilityLifecycleRegistration
{
    public static IServiceCollection AddProfileEligibilityLifecycle(this IServiceCollection services)
    {
        services.AddScoped<ProfileEligibilityLifecycle>();
        services.AddHostedService<ProfileEligibilityLifecycleWorker>();
        return services;
    }
}
