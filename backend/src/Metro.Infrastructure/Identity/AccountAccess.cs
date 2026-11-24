using System.Security.Claims;
using Metro.Infrastructure.Persistence.Context;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;

namespace Metro.Infrastructure.Identity;

/// <summary>Validate both the account and the server-side session on every bearer request.</summary>
public sealed class AccountAccess(ApplicationDbContext db) : JwtBearerEvents
{
    public override async Task TokenValidated(TokenValidatedContext context)
    {
        var principal = context.Principal!;
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
        var stamp = principal.FindFirstValue("security_stamp");
        var sessionId = principal.FindFirstValue("session_id");
        var ct = context.HttpContext.RequestAborted;
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId, ct);
        var now = DateTime.UtcNow;
        if (user is null || !user.EmailConfirmed || string.IsNullOrEmpty(stamp) || user.SecurityStamp != stamp ||
            !Guid.TryParse(sessionId, out var id) ||
            await db.UserProfiles.AnyAsync(x => x.UserId == userId &&
                (x.Status == "LOCKED" || x.Status == "DISABLED" || x.Status == "CLOSED"), ct) ||
            !await db.AuthSessions.AnyAsync(x => x.Id == id && x.UserId == userId && x.RevokedAt == null && x.ExpiresAt > now, ct))
            context.Fail("Account or session is no longer active.");
    }
}
