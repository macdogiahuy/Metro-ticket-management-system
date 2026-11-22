using Metro.Application.Abstractions.Payment;

namespace Metro.IntegrationTests.Payments;

/// <summary>Provider stub: deterministic reference, real HMAC verification (no outbound HTTP).</summary>
public sealed class StubPaymentGateway : IPaymentGateway
{
    public const string Secret = "be08-webhook-secret";

    public Task<(string ProviderReference, string? CheckoutUrl)> CreateTransactionAsync(
        string paymentCode, long amountMinor, string idempotencyKey, CancellationToken cancellationToken) =>
        Task.FromResult(($"STUB-{paymentCode}", (string?)null));

    public bool VerifySignature(string rawBody, string signature)
    {
        var expected = Convert.ToHexString(System.Security.Cryptography.HMACSHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(Secret), System.Text.Encoding.UTF8.GetBytes(rawBody))).ToLowerInvariant();
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(expected), System.Text.Encoding.UTF8.GetBytes(signature));
    }
}
