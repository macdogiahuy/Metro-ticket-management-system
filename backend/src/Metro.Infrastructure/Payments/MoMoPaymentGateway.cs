using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Metro.Application.Abstractions.Payment;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Metro.Infrastructure.Payments;

/// <summary>
/// MoMo TEST v2 gateway. Signs requests and verifies IPN signatures from raw body bytes.
/// No credentials or provider payloads are logged.
/// </summary>
public sealed class MoMoPaymentGateway(HttpClient http, IOptions<MoMoOptions> options) : IPaymentGateway
{
    public async Task<(string ProviderReference, string? CheckoutUrl)> CreateTransactionAsync(
        string paymentCode, long amountMinor, string idempotencyKey, CancellationToken cancellationToken)
    {
        var config = options.Value;
        if (string.IsNullOrWhiteSpace(config.AccessKey) || string.IsNullOrWhiteSpace(config.SecretKey))
            throw new PaymentException("PAYMENT_PROVIDER_NOT_CONFIGURED", StatusCodes.Status503ServiceUnavailable,
                "Payment provider credentials are not configured.");

        var body = new Dictionary<string, object>
        {
            ["partnerCode"] = config.PartnerCode,
            ["partnerName"] = "Metro Ticket",
            ["storeId"] = "MetroTicket",
            ["requestId"] = idempotencyKey,
            ["amount"] = amountMinor,
            ["orderId"] = paymentCode,
            ["orderInfo"] = "Metro ticket payment",
            ["redirectUrl"] = config.RedirectUrl,
            ["ipnUrl"] = config.IpnUrl,
            ["lang"] = "vi",
            ["extraData"] = string.Empty,
            ["requestType"] = "captureWallet",
        };
        var raw = JsonSerializer.Serialize(body);
        body["signature"] = Sign(raw, config.SecretKey);
        body["accessKey"] = config.AccessKey;

        using var request = new HttpRequestMessage(HttpMethod.Post, config.Endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
        using var response = await http.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new PaymentException("PAYMENT_PROVIDER_UNAVAILABLE", StatusCodes.Status502BadGateway,
                "Payment provider rejected the transaction request.");

        using var payload = JsonDocument.Parse(text);
        var root = payload.RootElement;
        var resultCode = root.TryGetProperty("resultCode", out var rc) ? rc.GetInt32() : -1;
        if (resultCode != 0)
            throw new PaymentException("PAYMENT_PROVIDER_ERROR", StatusCodes.Status502BadGateway,
                "Payment provider did not accept the transaction.");

        var reference = root.TryGetProperty("orderId", out var order) ? order.GetString() : paymentCode;
        var checkout = root.TryGetProperty("payUrl", out var url) ? url.GetString() : null;
        return (reference ?? paymentCode, checkout);
    }

    public bool VerifySignature(string rawBody, string signature)
    {
        var secret = options.Value.WebhookSecret;
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(signature)) return false;
        var expected = Sign(rawBody, secret);
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(signature));
    }

    private static string Sign(string rawData, string secretKey)
    {
        var bytes = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secretKey), Encoding.UTF8.GetBytes(rawData));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
