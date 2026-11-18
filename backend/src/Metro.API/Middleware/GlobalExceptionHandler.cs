using System.ComponentModel.DataAnnotations;
using FluentValidation;
using Metro.Application.Abstractions.Auth;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Metro.API.Middleware;

/// <summary>
/// Điểm tập trung mọi exception chưa được xử lý (DoD BE-01): ánh xạ sang RFC 9457
/// ProblemDetails thay vì để trả về trang lỗi mặc định của ASP.NET Core.
/// </summary>
public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    IHostEnvironment environment,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title) = exception switch
        {
            AuthException auth => (auth.Status, "Authentication failed"),
            FluentValidation.ValidationException => (StatusCodes.Status400BadRequest, "Validation failed"),
            UnauthorizedAccessException => (StatusCodes.Status403Forbidden, "Forbidden"),
            KeyNotFoundException => (StatusCodes.Status404NotFound, "Resource not found"),
            InvalidOperationException => (StatusCodes.Status409Conflict, "Invalid operation"),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred"),
        };

        if (status >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception on {Method} {Path}",
                httpContext.Request.Method, httpContext.Request.Path);
        }
        else
        {
            logger.LogWarning("Handled {ExceptionType} on {Method} {Path}: {Message}",
                exception.GetType().Name, httpContext.Request.Method, httpContext.Request.Path, exception.Message);
        }

        httpContext.Response.StatusCode = status;

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            // 5xx không lộ chi tiết exception ra ngoài; môi trường Development vẫn hiện để debug.
            Detail = status < StatusCodes.Status500InternalServerError || environment.IsDevelopment()
                ? exception.Message
                : null,
        };
        if (exception is AuthException authError)
        {
            problem.Extensions["code"] = authError.Code;
        }

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problem,
        });
    }
}
