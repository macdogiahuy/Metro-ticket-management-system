using Metro.Application.Abstractions.Gates;
using Metro.Application.Gates;

namespace Metro.UnitTests.Gates;

public sealed class GateLookupRulesTests
{
    [Theory]
    [InlineData("0912345678", "912345678")]
    [InlineData("+84 912 345 678", "912345678")]
    [InlineData("84912345678", "912345678")]
    [InlineData(" 0912.345.678 ", "912345678")]
    [InlineData(null, "")]
    public void Phone_is_reduced_to_national_digits(string? input, string expected)
        => Assert.Equal(expected, GateLookupRules.NormalizePhone(input));

    [Fact]
    public void Lookup_requires_exactly_one_identifier_and_a_reason()
    {
        GateLookupRules.Validate(new GateLookupRequest("T-1", "Counter support"));
        GateLookupRules.Validate(new GateLookupRequest("", "Counter support", "0912345678"));
        Assert.Throws<GateException>(() => GateLookupRules.Validate(new GateLookupRequest("", "Counter support")));
        Assert.Throws<GateException>(() => GateLookupRules.Validate(new GateLookupRequest("T-1", "Counter support", "0912345678")));
        Assert.Throws<GateException>(() => GateLookupRules.Validate(new GateLookupRequest("T-1", "")));
        Assert.Throws<GateException>(() => GateLookupRules.Validate(new GateLookupRequest("", "Support", "123")));
        Assert.Throws<GateException>(() => GateLookupRules.Validate(new GateLookupRequest(new string('x', 61), "Support")));
    }

    [Fact]
    public void Invalid_lookup_never_carries_the_phone_into_the_error()
    {
        var exception = Assert.Throws<GateException>(() =>
            GateLookupRules.Validate(new GateLookupRequest("T-1", "Support", "0912345678")));
        Assert.Equal("INVALID_GATE_LOOKUP", exception.Code);
        Assert.DoesNotContain("0912345678", exception.Message);
    }
}
