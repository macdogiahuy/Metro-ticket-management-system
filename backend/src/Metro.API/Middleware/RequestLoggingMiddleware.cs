using System.Diagnostics;

namespace Metro.API.Middleware;

/// <summary>
/// Ghi log một dòng cho mỗi HTTP request: method, path, status code và thời gian xử lý.
/// 4xx log ở mức Warning, 5xx ở mức Error để dễ lọc khi truy vết sự cố.
/// </summary>
public sealed class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var start = Stopwatch.GetTimestamp();

        try
        {
            await next(context);
        }
        finally
        {
            var elapsed = Stopwatch.GetElapsedTime(start);
            var statusCode = context.Response.StatusCode;
            var level = statusCode >= 500 ? LogLevel.Error
                : statusCode >= 400 ? LogLevel.Warning
                : LogLevel.Information;

            if (logger.IsEnabled(level))
            {
                logger.Log(level,
                    "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs:F1} ms",
                    context.Request.Method,
                    context.Request.Path,
                    statusCode,
                    elapsed.TotalMilliseconds);
            }
        }
    }
}
