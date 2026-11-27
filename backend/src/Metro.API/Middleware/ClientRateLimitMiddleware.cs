using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Metro.API.Configuration;
using Microsoft.Extensions.Options;

namespace Metro.API.Middleware;

/// <summary>BE-17: per-client fixed-window limit on selected paths; RFC 9457 429 with Retry-After.</summary>
public sealed class ClientRateLimitMiddleware(
    RequestDelegate next,
    IOptions<HardeningOptions> options,
    FixedWindowRateLimiter limiter,
    ILogger<ClientRateLimitMiddleware> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task InvokeAsync(HttpContext context)
    {
        var config = options.Value.RateLimiting;
        var path = context.Request.Path.Value ?? string.Empty;
        if (!config.Enabled || !config.Paths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            await next(context);
            return;
        }

        var user = context.User;
        var subject = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        var key = string.IsNullOrEmpty(subject)
            ? $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}"
            : $"sub:{subject}";

        if (!limiter.TryAcquire(key))
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers.RetryAfter = config.WindowSeconds.ToString();
            context.Response.ContentType = "application/problem+json";
            var body = JsonSerializer.Serialize(new
            {
                type = "https://tools.ietf.org/html/rfc9110#section-15.5.15",
                title = "Too Many Requests",
                status = StatusCodes.Status429TooManyRequests,
                code = "RATE_LIMITED",
            }, JsonOptions);
            await context.Response.WriteAsync(body, context.RequestAborted);
            logger.LogWarning("Rate limit {Limit}/{Window}s on {Prefix} for {KeyType}",
                config.PermitLimit, config.WindowSeconds,
                PiiRedactor.Redact(path.Split('/').LastOrDefault()), key.Split(':')[0]);
            return;
        }

        await next(context);
    }
}
