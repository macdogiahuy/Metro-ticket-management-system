using System.Security.Cryptography;
using System.Security.Claims;
using System.Text.Json;
using FluentValidation;
using Metro.Application.Abstractions.Payment;

namespace Metro.API.Endpoints;

public static class PaymentEndpoints
{
    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("Payment");

        group.MapPost("/payments", CreateAsync)
            .WithName("CreatePayment").RequireAuthorization()
            .Produces<PaymentResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/payments/{id:guid}", GetAsync)
            .WithName("GetPayment").RequireAuthorization()
            .Produces<PaymentResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/payments/{id:guid}/retry", RetryAsync)
            .WithName("RetryPayment").RequireAuthorization()
            .Produces<RetryPaymentResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status409Conflict);

        // Provider-called; authenticity is proven by the HMAC signature over the raw body.
        group.MapPost("/webhooks/momo", WebhookAsync)
            .WithName("MoMoWebhook").AllowAnonymous()
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        return app;
    }

    private static async Task<IResult> CreateAsync(
        CreatePaymentRequest request, IValidator<CreatePaymentRequest> validator,
        IPaymentService payments, ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var invalid = await validator.ValidateAsync(request, cancellationToken);
        if (!invalid.IsValid)
            return Results.ValidationProblem(invalid.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()),
                statusCode: StatusCodes.Status400BadRequest);

        var result = await payments.CreateAsync(request, UserId(principal), cancellationToken);
        return Results.Created($"/api/v1/payments/{result.Id}", result);
    }

    private static async Task<IResult> GetAsync(Guid id, IPaymentService payments, ClaimsPrincipal principal, CancellationToken cancellationToken) =>
        Results.Ok(await payments.GetAsync(id, UserId(principal), cancellationToken));

    private static async Task<IResult> RetryAsync(Guid id, IPaymentService payments, ClaimsPrincipal principal, CancellationToken cancellationToken) =>
        Results.Ok(await payments.RetryAsync(id, UserId(principal), cancellationToken));

    private static async Task<IResult> WebhookAsync(
        HttpRequest request, IPaymentService payments, IPaymentGateway gateway, CancellationToken cancellationToken)
    {
        // Read the exact bytes once: the signature is over the raw body, not re-serialized JSON.
        var raw = await new StreamReader(request.Body).ReadToEndAsync(cancellationToken);
        var signature = request.Headers.TryGetValue("X-MoMo-Signature", out var header)
            ? header.ToString()
            : request.Query["signature"].ToString();
        if (!gateway.VerifySignature(raw, signature))
            throw new PaymentException("INVALID_WEBHOOK_SIGNATURE", StatusCodes.Status401Unauthorized,
                "Webhook signature is invalid.");

        PaymentCallback callback;
        try
        {
            using var json = JsonDocument.Parse(raw);
            var root = json.RootElement;
            var amount = GetLong(root, "amount");
            var paymentId = GetGuid(root, "paymentId");
            callback = new PaymentCallback(
                GetString(root, "eventId"), GetString(root, "idempotencyKey"), paymentId,
                amount, GetString(root, "currency"),
                Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw))).ToLowerInvariant(),
                raw);
        }
        catch (Exception ex) when (ex is KeyNotFoundException or FormatException or JsonException or InvalidOperationException)
        {
            throw new PaymentException("INVALID_WEBHOOK_PAYLOAD", StatusCodes.Status400BadRequest,
                "Webhook payload is missing required fields.");
        }

        var issued = await payments.HandleWebhookAsync(callback, cancellationToken);
        return Results.Ok(new { resultCode = 0, message = "Success", issuedTickets = issued });
    }

    private static string UserId(ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? principal.FindFirstValue("sub")
        ?? throw new PaymentException("UNAUTHENTICATED", StatusCodes.Status401Unauthorized, "Missing subject claim.");

    private static string GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!
            : throw new KeyNotFoundException(name);

    private static Guid GetGuid(JsonElement root, string name) =>
        Guid.TryParse(GetString(root, name), out var value) ? value : throw new FormatException(name);

    private static long GetLong(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.TryGetInt64(out var number)
            ? number : throw new FormatException(name);
}
