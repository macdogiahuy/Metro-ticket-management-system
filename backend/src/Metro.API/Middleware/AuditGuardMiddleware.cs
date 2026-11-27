using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Metro.Application.Abstractions.MultiTenancy;
using Metro.Domain.Entities.Identity;
using Metro.Infrastructure.Persistence.Context;

namespace Metro.API.Middleware;

/// <summary>
/// BE-17: writes a request-level audit receipt for every successful admin write,
/// carrying the correlation id, actor and path so an audit always exists even if
/// a service emits only domain-specific records. Never logs request bodies or query strings.
/// </summary>
public sealed class AuditGuardMiddleware(RequestDelegate next, IServiceScopeFactory scopes, ILogger<AuditGuardMiddleware> logger)
{
    private static readonly string[] Prefixes = ["/api/v1/admin"];
    private static readonly string[] WriteMethods = ["POST", "PUT", "PATCH", "DELETE"];

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        var isAuditedWrite = WriteMethods.Contains(context.Request.Method, StringComparer.OrdinalIgnoreCase)
                             && Prefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));

        var rawCorrelation = context.Request.Headers["X-Correlation-Id"].ToString();
        var correlation = Guid.TryParse(rawCorrelation, out var parsed) ? parsed : Guid.NewGuid();
        var correlationText = correlation.ToString("N");
        context.Request.Headers["X-Correlation-Id"] = correlationText;
        context.Response.Headers["X-Correlation-Id"] = correlationText;

        var operatorContext = context.RequestServices.GetRequiredService<IOperatorContext>();

        await next(context);

        if (!isAuditedWrite || context.Response.StatusCode is < 200 or >= 300) return;

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var exists = await db.AuditLogs.IgnoreQueryFilters()
                .AnyAsync(x => x.CorrelationId == correlation, context.RequestAborted);
            if (exists) return;

            var user = context.User;
            var actor = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
            var operatorId = operatorContext.CurrentOperatorId;
            db.AuditLogs.Add(new AuditLog
            {
                CorrelationId = correlation,
                ActorUserId = actor,
                OperatorId = operatorId,
                DatabaseActor = "hardening-audit-guard",
                Action = "ADMIN_WRITE_AUDITED",
                EntityType = "http_request",
                EntityId = path,
                Outcome = "SUCCEEDED",
            });
            await db.SaveChangesAsync(context.RequestAborted);
        }
        catch (Exception ex)
        {
            // Audit failure must not change the already-completed response, but must be visible.
            logger.LogError("Audit guard failed for {Path}: {ErrorType}", PiiRedactor.Redact(path), ex.GetType().Name);
        }
    }
}
