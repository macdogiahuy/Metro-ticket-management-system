using System.Security.Claims;
using Metro.Application.Abstractions.Booking;
using Metro.Infrastructure.Booking;
using Microsoft.AspNetCore.Mvc;

namespace Metro.API.Endpoints;

public static class BookingEndpoints
{
    public static IEndpointRouteBuilder MapBookingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/orders").WithTags("Booking").RequireAuthorization();
        group.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store, private";
            return await next(context);
        });
        group.MapPost("", async (HttpContext http, string operatorCode, BookingSelection request,
            [FromHeader(Name = "Idempotency-Key")] string key, BookingService booking, CancellationToken ct) =>
        {
            var order = await booking.CreateAsync(Actor(http), operatorCode, request, Key(http, key), ct);
            return TypedResults.Created($"/api/v1/orders/{order.Id}", order);
        }).WithName("CreateBooking").WithSummary("Create a draft order and its first version")
            .ProducesProblem(400).ProducesProblem(403).ProducesProblem(409).ProducesProblem(422);
        group.MapGet("/{id:guid}", (HttpContext http, Guid id, BookingService booking, CancellationToken ct) =>
            booking.GetAsync(Actor(http), id, ct)).WithName("GetBooking").ProducesProblem(403).ProducesProblem(404);
        group.MapPut("/{id:guid}", (HttpContext http, Guid id, ReviseBookingRequest request,
            [FromHeader(Name = "Idempotency-Key")] string key, BookingService booking, CancellationToken ct) =>
            booking.ReviseAsync(Actor(http), id, request, Key(http, key), ct))
            .WithName("ReviseBooking").WithSummary("Replace the order version before payment")
            .ProducesProblem(403).ProducesProblem(404).ProducesProblem(409).ProducesProblem(422);
        group.MapPost("/{id:guid}/confirm", (HttpContext http, Guid id, ConfirmBookingRequest request,
            [FromHeader(Name = "Idempotency-Key")] string key, BookingService booking, CancellationToken ct) =>
            booking.ConfirmAsync(Actor(http), id, request, Key(http, key), ct))
            .WithName("ConfirmBooking").WithSummary("Confirm the quoted price and current order version")
            .ProducesProblem(403).ProducesProblem(404).ProducesProblem(409).ProducesProblem(422);
        group.MapGet("/consents", (HttpContext http, int? page, BookingService booking, CancellationToken ct) =>
            booking.PendingConsentsAsync(Actor(http), page ?? 1, ct)).WithName("ListPendingBookingConsents");
        group.MapPost("/{id:guid}/consent", (HttpContext http, Guid id, BookingConsentRequest request,
            [FromHeader(Name = "Idempotency-Key")] string key, BookingService booking, CancellationToken ct) =>
            booking.RespondAsync(Actor(http), id, request, Key(http, key), ct))
            .WithName("RespondToBookingConsent").WithSummary("Accept, decline or withdraw a version-specific invitation")
            .ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);
        group.MapDelete("/{id:guid}", (HttpContext http, Guid id, int orderVersion,
            [FromHeader(Name = "Idempotency-Key")] string key, BookingService booking, CancellationToken ct) =>
            booking.CancelAsync(Actor(http), id, orderVersion, Key(http, key), ct))
            .WithName("CancelBooking").WithSummary("Cancel an unpaid order before payment starts")
            .ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);
        group.MapGet("/{id:guid}/suggestion", (HttpContext http, Guid id, BookingService booking, CancellationToken ct) =>
            booking.GetSuggestionAsync(Actor(http), id, ct)).WithName("GetBookingSuggestion")
            .WithSummary("Get the first boarding trip suggestion without a capacity reservation")
            .ProducesProblem(403).ProducesProblem(404);
        return app;
    }

    private static string Actor(HttpContext http) =>
        http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? http.User.FindFirstValue("sub")
        ?? throw new BookingException("BOOKING_AUTH_REQUIRED", 401);

    private static string Key(HttpContext http, string key) => http.Request.Headers["Idempotency-Key"].Count == 1
        ? key : throw new BookingException("IDEMPOTENCY_KEY_REQUIRED", 400);
}
