using Metro.Application.Abstractions.Fare;
using Metro.Application.Fare;

namespace Metro.UnitTests.Fare;

public sealed class FareRulesTests
{
    [Theory]
    [InlineData("SINGLE_TRIP", null, null, "SINGLE_JOURNEY")]
    [InlineData("DAY", 1, "DAY", "NEXT_LOCAL_MIDNIGHT")]
    [InlineData("WEEK", 7, "DAY", "ELAPSED_DURATION")]
    [InlineData("MONTH", 30, "DAY", "ELAPSED_DURATION")]
    [InlineData("MONTH", 1, "MONTH", "NEXT_LOCAL_MIDNIGHT")]
    public void ValidateProductSpec_accepts_srs_v12_products(string type, int? duration, string? unit, string rule)
    {
        var result = FareRules.ValidateProductSpec(type, duration, unit, rule, capacityControlled: false);
        Assert.Equal(type, result.ProductType);
        Assert.Equal(duration, result.DurationValue);
        Assert.Equal(unit, result.DurationUnit);
        Assert.Equal(rule, result.ExpiryRule);
    }

    [Theory]
    [InlineData("DAY", null, "DAY", "ELAPSED_DURATION", "PRODUCT_DURATION_REQUIRED")]
    [InlineData("WEEK", 0, "DAY", "ELAPSED_DURATION", "PRODUCT_DURATION_REQUIRED")]
    [InlineData("MONTH", 30, null, "ELAPSED_DURATION", "PRODUCT_DURATION_REQUIRED")]
    [InlineData("DAY", 3, "DAY", "ELAPSED_DURATION", "UNSUPPORTED_PRODUCT_DURATION")] // 3-day ticket prohibited (BR-002)
    [InlineData("MULTI_MONTH", 3, "MONTH", "ELAPSED_DURATION", "UNSUPPORTED_PRODUCT_TYPE")]
    [InlineData("THREE_DAY", 3, "DAY", "ELAPSED_DURATION", "UNSUPPORTED_PRODUCT_TYPE")]
    [InlineData("SINGLE_TRIP", 1, "DAY", "SINGLE_JOURNEY", "INVALID_SINGLE_TRIP_DURATION")]
    [InlineData("SINGLE_TRIP", null, null, "ELAPSED_DURATION", "INVALID_EXPIRY_RULE")]
    [InlineData("DAY", 1, "DAY", "SINGLE_JOURNEY", "INVALID_EXPIRY_RULE")]
    public void ValidateProductSpec_blocks_missing_duration_and_invalid_products(
        string type, int? duration, string? unit, string rule, string expectedCode)
    {
        var ex = Assert.Throws<FareException>(() => FareRules.ValidateProductSpec(type, duration, unit, rule, false));
        Assert.Equal(400, ex.Status);
        Assert.Equal(expectedCode, ex.Code);
    }

    [Fact]
    public void ValidateProductSpec_rejects_legacy_capacity_controlled_product()
    {
        var ex = Assert.Throws<FareException>(() => FareRules.ValidateProductSpec("SINGLE_TRIP", null, null, "SINGLE_JOURNEY", true));
        Assert.Equal("CAPACITY_CONTROLLED_NOT_SUPPORTED", ex.Code);
    }

    [Theory]
    [InlineData(500.5, 501)]
    [InlineData(500.49, 500)]
    [InlineData(750.5, 751)]
    [InlineData(6300.0, 6300)]
    public void RoundHalfUp_rounds_0_5_upward(decimal raw, long expected) =>
        Assert.Equal(expected, FareRules.RoundHalfUp(raw));

    [Fact]
    public void CalculateAgeYears_and_VietnamDate_respect_utc_plus_7_boundaries()
    {
        // 2026-11-21 17:00:00Z is 2026-11-22 00:00:00+07 (next business day in Vietnam).
        Assert.Equal(new DateOnly(2026, 11, 22), FareRules.ToVietnamDate(new DateTime(2026, 11, 21, 17, 0, 0, DateTimeKind.Utc)));
        Assert.Equal(new DateOnly(2026, 11, 21), FareRules.ToVietnamDate(new DateTime(2026, 11, 21, 16, 59, 59, DateTimeKind.Utc)));

        // Child turning 6: day before = 5 (eligible), birthday = 6 (ineligible).
        Assert.Equal(5, FareRules.CalculateAgeYears(new DateOnly(2020, 11, 22), new DateOnly(2026, 11, 21)));
        Assert.Equal(6, FareRules.CalculateAgeYears(new DateOnly(2020, 11, 22), new DateOnly(2026, 11, 22)));

        // Elderly turning 60: day before = 59 (ineligible), birthday = 60 (eligible).
        Assert.Equal(59, FareRules.CalculateAgeYears(new DateOnly(1966, 11, 22), new DateOnly(2026, 11, 21)));
        Assert.Equal(60, FareRules.CalculateAgeYears(new DateOnly(1966, 11, 22), new DateOnly(2026, 11, 22)));
    }

    [Theory]
    [InlineData(999, false)]
    [InlineData(50_000_001, false)]
    public void ValidatePaidRange_rejects_out_of_momo_bounds(long amount, bool isFree)
    {
        var ex = Assert.Throws<FareException>(() => FareRules.ValidatePaidRange(amount, isFree));
        Assert.Equal(422, ex.Status);
        Assert.Equal("FARE_OUT_OF_PAYMENT_RANGE", ex.Code);
    }

    [Fact]
    public void ValidatePaidRange_allows_zero_for_free_ticket_and_momo_boundaries()
    {
        FareRules.ValidatePaidRange(0, isFreeTicket: true);
        FareRules.ValidatePaidRange(1_000, isFreeTicket: false);
        FareRules.ValidatePaidRange(50_000_000, isFreeTicket: false);
    }
}
