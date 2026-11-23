using System.Security.Claims;
using Metro.Domain.Entities.Identity;
using Metro.Infrastructure.Persistence.Context;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace Metro.API.Middleware;

/// <summary>Record rejected authenticated attempts to access administrative data.</summary>
public sealed class AdministrationAuthorizationHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy,
        PolicyAuthorizationResult result)
    {
        if (result.Forbidden && context.Request.Path.StartsWithSegments("/api/v1/admin"))
        {
            var db = context.RequestServices.GetRequiredService<ApplicationDbContext>();
            db.AuditLogs.Add(new AuditLog
            {
                ActorUserId = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub"),
                Action = "ADMIN_ACCESS_DENIED", EntityType = "administration",
                EntityId = context.Request.RouteValues["userId"]?.ToString() ?? "accounts",
                Reason = "ADMIN_REQUIRED", Outcome = "DENIED",
            });
            await db.SaveChangesAsync(context.RequestAborted);
        }
        await _default.HandleAsync(next, context, policy, result);
    }
}
