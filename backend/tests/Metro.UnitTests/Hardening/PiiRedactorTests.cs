using Metro.API.Middleware;

namespace Metro.UnitTests.Hardening;

public sealed class PiiRedactorTests
{
    [Fact]
    public void Redacts_email_phone_token_and_six_digit_codes()
    {
        var input = "user nguyen.van.a@gmail.com phone 0912345678 code 123456 Bearer abcdefghijklmnopqrstuvwxyz1234";
        var redacted = PiiRedactor.Redact(input);

        Assert.DoesNotContain("nguyen.van.a@gmail.com", redacted);
        Assert.DoesNotContain("0912345678", redacted);
        Assert.DoesNotContain("123456", redacted);
        Assert.DoesNotContain("abcdefghijklmnopqrstuvwxyz1234", redacted);
        Assert.Contains("[email]", redacted);
        Assert.Contains("[phone]", redacted);
        Assert.Contains("[token]", redacted);
    }

    [Fact]
    public void Keeps_non_pii_text_and_handles_null()
    {
        Assert.Equal("GET /api/v1/health", PiiRedactor.Redact("GET /api/v1/health"));
        Assert.Equal(string.Empty, PiiRedactor.Redact(null));
    }
}
