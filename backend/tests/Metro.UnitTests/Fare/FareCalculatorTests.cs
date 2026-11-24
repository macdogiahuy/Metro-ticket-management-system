using Metro.Application.Abstractions.Fare;
using Metro.Application.Fare;

namespace Metro.UnitTests.Fare;

public sealed class FareCalculatorTests
{
    private readonly FareCalculator _sut = new();
    private static readonly Guid ProductSingle = Guid.NewGuid();
    private static readonly Guid ProductDay = Guid.NewGuid();
    private static readonly Guid ProductWeek = Guid.NewGuid();
    private static readonly Guid ProductMonth = Guid.NewGuid();
    private static readonly Guid Line1 = Guid.NewGuid();
    private static readonly Guid S1 = Guid.NewGuid();
    private static readonly Guid S2 = Guid.NewGuid();
    private static readonly DateOnly RefDate = new(2026, 11, 22);

    private static IReadOnlyList<FareMatrixCandidate> DemoMatrix() =>
    [
        new(Guid.NewGuid(), ProductSingle, null, null, null, "ANY", "ORDINARY", 5_000, true),
        new(Guid.NewGuid(), ProductDay, null, null, null, "ANY", "ORDINARY", 30_000, true),
        new(Guid.NewGuid(), ProductWeek, null, null, null, "ANY", "ORDINARY", 150_000, true),
        new(Guid.NewGuid(), ProductMonth, null, null, null, "ANY", "ORDINARY", 500_000, true),
    ];

    private static IReadOnlyList<EligibilityPolicyRule> DemoPolicies() =>
    [
        new(Guid.NewGuid(), "STUDENT-10", 1, "STUDENT", "PERCENT", 10m, []),
        new(Guid.NewGuid(), "ELDERLY-20", 1, "ELDERLY", "PERCENT", 20m, []),
        new(Guid.NewGuid(), "CHILD-FREE", 1, "CHILD_UNDER_6", "FREE", 100m, ["SINGLE"]),
    ];

    [Theory]
    [InlineData(1, "ORDINARY", 5_000, 0, 5_000)]
    [InlineData(3, "ORDINARY", 7_000, 0, 7_000)]
    [InlineData(4, "ORDINARY", 8_000, 0, 8_000)]
    [InlineData(1, "STUDENT", 5_000, 500, 4_500)]
    [InlineData(3, "STUDENT", 7_000, 700, 6_300)]
    [InlineData(3, "ELDERLY", 7_000, 1_400, 5_600)]
    public void SingleTrip_segment_formula_and_discounts_match_AC_X04_and_AC_X23(
        int segments, string group, long expectedBase, long expectedDiscount, long expectedTotal)
    {
        var res = _sut.Calculate(new FareCalculationInput(
            ProductSingle, "SINGLE", "SINGLE_TRIP", Line1, S1, S2, segments, "ANY", group, RefDate, DemoMatrix(), DemoPolicies()));

        Assert.Equal(expectedBase, res.BaseAmountMinor);
        Assert.Equal(expectedDiscount, res.DiscountAmountMinor);
        Assert.Equal(expectedTotal, res.TotalAmountMinor);
        Assert.Equal(group, res.AppliedEligibilityGroup);
        Assert.Equal("SEGMENT_FORMULA", res.PricingSource);
    }

    [Theory]
    [InlineData("DAY", 30_000, 6_000, 24_000)]
    [InlineData("WEEK", 150_000, 30_000, 120_000)]
    [InlineData("MONTH", 500_000, 100_000, 400_000)]
    public void Periodic_passes_apply_20_percent_elderly_discount(string type, long baseAmt, long discount, long total)
    {
        var pid = type switch { "DAY" => ProductDay, "WEEK" => ProductWeek, _ => ProductMonth };
        var res = _sut.Calculate(new FareCalculationInput(
            pid, type, type, null, null, null, null, "NON_CASH", "ELDERLY", RefDate, DemoMatrix(), DemoPolicies(),
            new PassengerEligibilityContext(true, new DateOnly(1966, 11, 22), true, RefDate.AddMonths(-1), RefDate.AddMonths(6))));

        Assert.Equal(baseAmt, res.BaseAmountMinor);
        Assert.Equal(discount, res.DiscountAmountMinor);
        Assert.Equal(total, res.TotalAmountMinor);
        Assert.Equal("ELDERLY", res.AppliedEligibilityGroup);
        Assert.False(res.FallbackToOrdinary);
    }

    [Fact]
    public void HalfUp_rounding_applies_to_fractional_vnd_discount()
    {
        var entries = new[] { new FareMatrixCandidate(Guid.NewGuid(), ProductDay, null, null, null, "ANY", "ORDINARY", 5_555, true) };
        var res = _sut.Calculate(new FareCalculationInput(
            ProductDay, "DAY", "DAY", null, null, null, null, "ANY", "STUDENT", RefDate, entries, DemoPolicies()));

        // 10% of 5,555 = 555.5 -> rounds half-up to 556 discount -> 4,999 total.
        Assert.Equal(556, res.DiscountAmountMinor);
        Assert.Equal(4_999, res.TotalAmountMinor);
    }

    [Fact]
    public void Elderly_under_60_or_unverified_adult_falls_back_to_ordinary_fare()
    {
        // Day before 60th birthday -> age 59 -> ORDINARY fallback (BR-071).
        var underage = _sut.Calculate(new FareCalculationInput(
            ProductDay, "DAY", "DAY", null, null, null, null, "ANY", "ELDERLY", RefDate, DemoMatrix(), DemoPolicies(),
            new PassengerEligibilityContext(true, new DateOnly(1966, 11, 23), true, RefDate.AddDays(-10), RefDate.AddDays(30))));
        Assert.Equal("ORDINARY", underage.AppliedEligibilityGroup);
        Assert.True(underage.FallbackToOrdinary);
        Assert.Equal(30_000, underage.TotalAmountMinor);

        // Expired student profile -> ORDINARY fallback (BR-019).
        var expired = _sut.Calculate(new FareCalculationInput(
            ProductDay, "DAY", "DAY", null, null, null, null, "ANY", "STUDENT", RefDate, DemoMatrix(), DemoPolicies(),
            new PassengerEligibilityContext(true, new DateOnly(2004, 1, 1), true, RefDate.AddMonths(-13), RefDate.AddDays(-1))));
        Assert.Equal("ORDINARY", expired.AppliedEligibilityGroup);
        Assert.True(expired.FallbackToOrdinary);
        Assert.Equal(30_000, expired.TotalAmountMinor);
    }

    [Fact]
    public void Child_under_6_rules_allow_free_single_trip_and_never_fallback_to_paid()
    {
        // 5 years old with approved application -> 0 VND free ticket.
        var ok = _sut.Calculate(new FareCalculationInput(
            ProductSingle, "SINGLE", "SINGLE_TRIP", Line1, S1, S2, 3, "ANY", "CHILD_UNDER_6", RefDate, DemoMatrix(), DemoPolicies(),
            new PassengerEligibilityContext(true, new DateOnly(2020, 11, 23), true, RefDate.AddDays(-5), RefDate.AddDays(5))));
        Assert.Equal(7_000, ok.BaseAmountMinor);
        Assert.Equal(7_000, ok.DiscountAmountMinor);
        Assert.Equal(0, ok.TotalAmountMinor);
        Assert.True(ok.IsFreeTicket);

        // Turning 6 on travelDate -> rejected (no paid fallback, BR-021).
        var turnedSix = Assert.Throws<FareException>(() => _sut.Calculate(new FareCalculationInput(
            ProductSingle, "SINGLE", "SINGLE_TRIP", Line1, S1, S2, 3, "ANY", "CHILD_UNDER_6", RefDate, DemoMatrix(), DemoPolicies(),
            new PassengerEligibilityContext(true, new DateOnly(2020, 11, 22), true, RefDate.AddDays(-5), RefDate.AddDays(5)))));
        Assert.Equal("CHILD_FREE_INELIGIBLE", turnedSix.Code);

        // Periodic pass -> rejected (BR-021).
        var periodic = Assert.Throws<FareException>(() => _sut.Calculate(new FareCalculationInput(
            ProductMonth, "MONTH", "MONTH", null, null, null, null, "ANY", "CHILD_UNDER_6", RefDate, DemoMatrix(), DemoPolicies())));
        Assert.Equal("CHILD_SINGLE_TRIP_ONLY", periodic.Code);
    }

    [Fact]
    public void NullSafe_duplicate_key_not_sellable_and_explicit_od_override_behave_deterministically()
    {
        // Duplicate NULL-scope key -> 409 FARE_MATRIX_DUPLICATE.
        var dup = new[]
        {
            new FareMatrixCandidate(Guid.NewGuid(), ProductDay, null, null, null, "ANY", "ORDINARY", 30_000, true),
            new FareMatrixCandidate(Guid.NewGuid(), ProductDay, null, null, null, "ANY", "ORDINARY", 35_000, true),
        };
        var dupEx = Assert.Throws<FareException>(() => _sut.Calculate(new FareCalculationInput(
            ProductDay, "DAY", "DAY", null, null, null, null, "ANY", "ORDINARY", RefDate, dup, [])));
        Assert.Equal("FARE_MATRIX_DUPLICATE", dupEx.Code);

        // Explicit OD overrides formula and respects Sellable = false.
        var notSellable = new[] { new FareMatrixCandidate(Guid.NewGuid(), ProductSingle, Line1, S1, S2, "ANY", "ORDINARY", 9_000, false) };
        var sellEx = Assert.Throws<FareException>(() => _sut.Calculate(new FareCalculationInput(
            ProductSingle, "SINGLE", "SINGLE_TRIP", Line1, S1, S2, 2, "ANY", "ORDINARY", RefDate, notSellable, [])));
        Assert.Equal("FARE_NOT_SELLABLE", sellEx.Code);

        // Explicit STUDENT matrix entry does NOT double-discount.
        var explicitStudent = new[]
        {
            new FareMatrixCandidate(Guid.NewGuid(), ProductDay, null, null, null, "ANY", "ORDINARY", 30_000, true),
            new FareMatrixCandidate(Guid.NewGuid(), ProductDay, null, null, null, "ANY", "STUDENT", 25_000, true),
        };
        var overrideRes = _sut.Calculate(new FareCalculationInput(
            ProductDay, "DAY", "DAY", null, null, null, null, "ANY", "STUDENT", RefDate, explicitStudent, DemoPolicies()));
        Assert.Equal(30_000, overrideRes.BaseAmountMinor);
        Assert.Equal(5_000, overrideRes.DiscountAmountMinor);
        Assert.Equal(25_000, overrideRes.TotalAmountMinor);
    }
}
