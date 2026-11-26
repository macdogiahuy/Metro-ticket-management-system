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
    public sealed record Reference(string KeyId, Guid Operator, Guid TicketReference, Guid Credential, int Version);
    /// <summary>Parse routing fields only. Authorization still requires the current stored credential and token hash.</summary>
    public static Reference? Read(string token)
    {
        var p = token.Split('.');
        return p.Length == 7 && p[0] == "M1" && p[1].Length is > 0 and <= 40 &&
            p[1].All(c => char.IsAsciiLetterOrDigit(c) || c == '-') &&
            Guid.TryParseExact(p[2], "N", out var op) && Guid.TryParseExact(p[3], "N", out var ticket) &&
            Guid.TryParseExact(p[4], "N", out var credential) && int.TryParse(p[5], out var version) && version > 0 &&
            p[6].Length == 64 && p[6].All(char.IsAsciiHexDigit)
            ? new(p[1], op, ticket, credential, version) : null;
    }
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
