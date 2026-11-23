using System.Security.Cryptography;
using System.Text;
using Metro.Application.Abstractions.Ticketing;
using Microsoft.Extensions.Options;
using QRCoder;

namespace Metro.Infrastructure.Ticketing;

public sealed class TicketQrOptions
{
    public bool WorkerEnabled { get; set; } = true;
    public string ActiveKeyId { get; set; } = string.Empty;
    public Dictionary<string, string> Keys { get; set; } = new();
}

public sealed class TicketQrCodec(IOptions<TicketQrOptions> options)
{
    public string ActiveKeyId => options.Value.ActiveKeyId;
    public string Token(Guid op, Guid ticketReference, Guid credential, int version, string keyId)
    {
        if (string.IsNullOrWhiteSpace(keyId) || keyId.Length > 40 || keyId.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
            throw new TicketException("QR_KEY_UNAVAILABLE", 503);
        if (!options.Value.Keys.TryGetValue(keyId, out var encoded)) throw new TicketException("QR_KEY_UNAVAILABLE", 503);
        byte[] key;
        try { key = Convert.FromBase64String(encoded); }
        catch (FormatException) { throw new TicketException("QR_KEY_UNAVAILABLE", 503); }
        if (key.Length < 32) throw new TicketException("QR_KEY_UNAVAILABLE", 503);
        var payload = $"M1.{keyId}.{op:N}.{ticketReference:N}.{credential:N}.{version}";
        try { return payload + "." + Convert.ToHexStringLower(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(payload))); }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
    public static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    public static bool Matches(string token, string expected) => expected.Length == 64 &&
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Hash(token)), Encoding.ASCII.GetBytes(expected));
    public static byte[] Png(string token) => PngByteQRCodeHelper.GetQRCode(token, QRCodeGenerator.ECCLevel.Q, 8);
}
