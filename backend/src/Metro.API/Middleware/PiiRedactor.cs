using System.Text.RegularExpressions;

namespace Metro.API.Middleware;

/// <summary>BE-17: deterministic redaction of common PII/secret shapes before anything is logged.</summary>
public static partial class PiiRedactor
{
    public static string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var text = Email().Replace(value, "[email]");
        text = Phone().Replace(text, "[phone]");
        text = BearerToken().Replace(text, "Bearer [token]");
        text = Jwt().Replace(text, "[token]");
        return ChallengeCode().Replace(text, "[code]");
    }

    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}", RegexOptions.Compiled)]
    private static partial Regex Email();

    // VN mobile numbers (09x/01xxx/084...), 9-11 digits, allow separators.
    [GeneratedRegex(@"(?<!\d)(?:\+?84|0)(?:\d[\s.\-]?){8,10}\d(?!\d)", RegexOptions.Compiled)]
    private static partial Regex Phone();

    [GeneratedRegex(@"Bearer\s+[A-Za-z0-9\-._~+/]{16,}=*", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex BearerToken();

    // JWT-like three base64url segments.
    [GeneratedRegex(@"\beyJ[A-Za-z0-9_\-]{8,}\.[A-Za-z0-9_\-]{8,}\.[A-Za-z0-9_\-]{8,}\b", RegexOptions.Compiled)]
    private static partial Regex Jwt();

    // 6-digit email verification / OTP codes anywhere in a message.
    [GeneratedRegex(@"(?<!\d)\d{6}(?!\d)", RegexOptions.Compiled)]
    private static partial Regex ChallengeCode();
}
