using System.Security.Cryptography;
using System.Text;
using Metro.Application.Abstractions.Payment;
using Metro.Infrastructure.Payments;
using Microsoft.Extensions.Options;

namespace Metro.UnitTests.Payment;

public sealed class MoMoPaymentGatewayTests
{
    private const string Secret = "test-webhook-secret";

    private static MoMoPaymentGateway Gateway() => new(
        new HttpClient(),
        Options.Create(new MoMoOptions { WebhookSecret = Secret }));

    [Fact]
    public void VerifySignature_accepts_exact_hmac_and_rejects_tampered_body()
    {
        var gateway = Gateway();
        const string body = "{\"eventId\":\"evt-1\",\"amount\":15000}";
        var signature = Hmac(body, Secret);

        Assert.True(gateway.VerifySignature(body, signature));
        Assert.False(gateway.VerifySignature(body + " ", signature));
        Assert.False(gateway.VerifySignature(body, Hmac(body, "wrong-secret")));
        Assert.False(gateway.VerifySignature(body, string.Empty));
    }

    private static string Hmac(string raw, string secret) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(raw)))
            .ToLowerInvariant();
}
