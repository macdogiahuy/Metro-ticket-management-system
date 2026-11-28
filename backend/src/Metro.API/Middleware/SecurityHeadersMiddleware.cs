namespace Metro.API.Middleware;

/// <summary>BE-17: fixed security headers on every response, no-store for sensitive groups.</summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    private static readonly string[] SensitivePrefixes =
        ["/api/v1/admin", "/api/v1/reports", "/api/v1/me", "/api/v1/tickets", "/api/v1/orders", "/api/v1/payments"];

    public async Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'";
        headers["Permissions-Policy"] = "geolocation=(), microphone=(), camera=()";

        var path = context.Request.Path.Value ?? string.Empty;
        if (SensitivePrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            headers["Cache-Control"] = "no-store";

        await next(context);
    }
}
