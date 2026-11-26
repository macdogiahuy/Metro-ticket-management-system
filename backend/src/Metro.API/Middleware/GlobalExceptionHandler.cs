using System.ComponentModel.DataAnnotations;
using FluentValidation;
using Metro.Application.Abstractions.Auth;
using Metro.Application.Abstractions.Booking;
using Metro.Application.Abstractions.Catalog;
using Metro.Application.Abstractions.Fare;
using Metro.Application.Abstractions.Payment;
using Metro.Application.Abstractions.Reports;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

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
        var pg = exception is DbUpdateException dbEx ? dbEx.InnerException as PostgresException : exception as PostgresException;
        (int status, string title, string? safeDetail, string? errorCode) = exception switch
        {
            BadHttpRequestException => (StatusCodes.Status400BadRequest, "Invalid request", exception.Message, (string?)null),
            Metro.Application.Abstractions.Ticketing.TicketException ticket => (ticket.Status, "Ticket request failed", ticket.Message, (string?)ticket.Code),
            BookingException booking => (booking.Status, "Booking request failed", booking.Code, booking.Code),
            AuthException auth => (auth.Status, "Authentication failed", auth.Message, (string?)auth.Code),
            PaymentException payment => (payment.Status, payment.Status switch
            {
                StatusCodes.Status401Unauthorized => "Payment authentication failed",
                StatusCodes.Status403Forbidden => "Payment forbidden",
                StatusCodes.Status404NotFound => "Payment resource not found",
                StatusCodes.Status409Conflict => "Payment state conflict",
                _ => "Payment provider error",
            }, payment.Message, payment.Code),
            ReportException report => (report.Status, report.Status switch
            {
                StatusCodes.Status401Unauthorized => "Report authentication failed",
                StatusCodes.Status403Forbidden => "Report forbidden",
                StatusCodes.Status422UnprocessableEntity => "Report export limit exceeded",
                _ => "Invalid report request",
            }, report.Message, report.Code),
            CatalogException catalog => (catalog.Status, catalog.Status switch
            {
                StatusCodes.Status404NotFound => "Resource not found",
                StatusCodes.Status422UnprocessableEntity => "Route unavailable",
                _ => "Invalid catalog request",
            }, catalog.Message, catalog.Code),
            FareException fare => (fare.Status, fare.Status switch
            {
                StatusCodes.Status404NotFound => "Fare resource not found",
                StatusCodes.Status409Conflict => "Fare conflict",
                StatusCodes.Status422UnprocessableEntity => "Fare rule violation",
                _ => "Invalid fare request",
            }, fare.Message, fare.Code),
            _ when pg is { SqlState: PostgresErrorCodes.UniqueViolation } =>
                (StatusCodes.Status409Conflict, "Duplicate resource conflict", "A conflicting record with the same unique key already exists.", "DATABASE_UNIQUE_CONFLICT"),
            _ when pg is { SqlState: PostgresErrorCodes.ExclusionViolation } =>
                (StatusCodes.Status409Conflict, "Overlapping window conflict", "The requested effective window overlaps an existing active record.", "DATABASE_EXCLUSION_CONFLICT"),
            _ when pg is { SqlState: PostgresErrorCodes.CheckViolation } =>
                (StatusCodes.Status400BadRequest, "Constraint validation failed", "The request violates a database validation constraint.", "DATABASE_CHECK_VIOLATION"),
            _ when pg is { SqlState: PostgresErrorCodes.ForeignKeyViolation } =>
                (StatusCodes.Status409Conflict, "Reference conflict", "The referenced resource does not exist or cannot be removed.", "DATABASE_REFERENCE_CONFLICT"),
            _ when pg is { SqlState: PostgresErrorCodes.NoDataFound } =>
                (StatusCodes.Status404NotFound, "Resource not found", "The requested resource was not found.", "DATABASE_NOT_FOUND"),
            _ when pg is { SqlState: PostgresErrorCodes.InvalidParameterValue } =>
                (StatusCodes.Status422UnprocessableEntity, "Invalid business state", "The request does not satisfy the current business conditions.", "DATABASE_INVALID_STATE"),
            _ when pg is { SqlState: PostgresErrorCodes.InvalidAuthorizationSpecification } =>
                (StatusCodes.Status403Forbidden, "Forbidden", "The requested operation is not permitted.", "DATABASE_FORBIDDEN"),
            _ when pg is { SqlState: PostgresErrorCodes.RaiseException } =>
                (StatusCodes.Status409Conflict, "Business state conflict", "The operation conflicts with the current resource state.", "DATABASE_STATE_CONFLICT"),
            FluentValidation.ValidationException => (StatusCodes.Status400BadRequest, "Validation failed", exception.Message, null),
            UnauthorizedAccessException => (StatusCodes.Status403Forbidden, "Forbidden", exception.Message, null),
            KeyNotFoundException => (StatusCodes.Status404NotFound, "Resource not found", exception.Message, null),
            InvalidOperationException => (StatusCodes.Status409Conflict, "Invalid operation", exception.Message, null),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred", environment.IsDevelopment() ? exception.Message : null, null),
        };

        if (status >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception on {Method} {Path}",
                httpContext.Request.Method, httpContext.Request.Path);
        }
        else
        {
            logger.LogWarning("Handled {ExceptionType} ({ErrorCode}) on {Method} {Path}",
                exception.GetType().Name, errorCode ?? "NONE", httpContext.Request.Method, httpContext.Request.Path);
        }

        httpContext.Response.StatusCode = status;

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = safeDetail,
        };
        if (errorCode is not null)
        {
            problem.Extensions["code"] = errorCode;
        }

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problem,
        });
    }
}
