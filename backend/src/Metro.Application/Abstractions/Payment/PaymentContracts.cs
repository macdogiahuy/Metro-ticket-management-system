namespace Metro.Application.Abstractions.Payment;

/// <summary>BE-08 contracts: POST /payments, /payments/{id}/retry, /webhooks/momo.</summary>
public sealed record CreatePaymentRequest(Guid OrderId, string? IdempotencyKey);
public sealed record PaymentResponse(
    Guid Id, Guid OrderId, string PaymentCode, string Provider, string Status,
    long AmountMinor, string Currency, string? ProviderPaymentReference, string IdempotencyKey,
    DateTimeOffset InitiatedAt, string? CheckoutUrl);

public sealed record RetryPaymentResult(PaymentResponse Payment, bool Retried);

/// <summary>Parsed, verified provider callback. All fields come from the signed provider body.</summary>
public sealed record PaymentCallback(
    string ProviderEventId, string EventIdempotencyKey, Guid PaymentId,
    long AmountMinor, string Currency, string PayloadHash, string RawBody);

public interface IPaymentService
{
    Task<PaymentResponse> CreateAsync(CreatePaymentRequest request, string userId, CancellationToken cancellationToken);
    Task<PaymentResponse> GetAsync(Guid paymentId, string userId, CancellationToken cancellationToken);
    Task<RetryPaymentResult> RetryAsync(Guid paymentId, string userId, CancellationToken cancellationToken);
    Task<int> HandleWebhookAsync(PaymentCallback callback, CancellationToken cancellationToken);
}

/// <summary>Outbound gateway + webhook signature check. Implementation owns provider HTTP details.</summary>
public interface IPaymentGateway
{
    /// <summary>Returns a provider reference and optional hosted-checkout URL.</summary>
    Task<(string ProviderReference, string? CheckoutUrl)> CreateTransactionAsync(
        string paymentCode, long amountMinor, string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>True when the provider signature covers the exact raw body bytes received.</summary>
    bool VerifySignature(string rawBody, string signature);
}

public sealed class PaymentException(string code, int status, string message) : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
}

public sealed class MoMoOptions
{
    public string Provider { get; set; } = "MOMO_TEST";
    public string Endpoint { get; set; } = "https://test-payment.momo.vn/v2/gateway/api/create";
    public string PartnerCode { get; set; } = "MOMOBKUN20180529";
    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;
    public string RedirectUrl { get; set; } = "http://localhost:5173/payment/result";
    public string IpnUrl { get; set; } = string.Empty;
}
