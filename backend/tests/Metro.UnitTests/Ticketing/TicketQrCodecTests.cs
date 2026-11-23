using System.Security.Cryptography;
using Metro.Infrastructure.Ticketing;
using Microsoft.Extensions.Options;

namespace Metro.UnitTests.Ticketing;

public sealed class TicketQrCodecTests
{
    [Fact]
    public void QR_token_is_deterministic_signed_versioned_and_contains_no_PII()
    {
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var options = Options.Create(new TicketQrOptions { ActiveKeyId = "v1", Keys = new() { ["v1"] = key } });
        var a = new TicketQrCodec(options); var op = Guid.NewGuid(); var reference = Guid.NewGuid(); var id = Guid.NewGuid();
        var token = a.Token(op, reference, id, 1, "v1");
        Assert.Equal(token, new TicketQrCodec(options).Token(op, reference, id, 1, "v1"));
        Assert.NotEqual(token, a.Token(op, reference, id, 2, "v1"));
        Assert.True(TicketQrCodec.Matches(token, TicketQrCodec.Hash(token)));
        Assert.False(TicketQrCodec.Matches(token + "x", TicketQrCodec.Hash(token)));
        Assert.DoesNotContain("@", token); Assert.DoesNotContain(key, token);
    }
}
