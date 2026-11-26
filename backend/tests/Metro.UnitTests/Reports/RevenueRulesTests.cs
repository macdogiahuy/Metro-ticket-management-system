using Metro.Application.Reports;

namespace Metro.UnitTests.Reports;

public sealed class RevenueRulesTests
{
    [Theory]
    [InlineData(9999, false)]
    [InlineData(10000, false)]
    [InlineData(10001, true)]
    public void Export_limit_is_inclusive_after_grouping(int rows, bool expected) =>
        Assert.Equal(expected, RevenueRules.ExceedsExportLimit(rows));

    [Theory]
    [InlineData(10000L, 10000L, 10000L)]
    [InlineData(12000L, 9000L, 9000L)]
    [InlineData(7000L, 9000L, 7000L)]
    [InlineData(0L, 9000L, 0L)]
    public void Valid_paid_value_is_limited_to_order_value(
        long collected,
        long orderTotal,
        long expected)
    {
        var actual = RevenueRules.ValidPaidOrderValue(
            collected,
            orderTotal);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(10000L, 10000L, 0L)]
    [InlineData(12000L, 9000L, 3000L)]
    [InlineData(7000L, 9000L, 0L)]
    [InlineData(0L, 9000L, 0L)]
    public void Excess_is_only_the_amount_above_order_value(
        long collected,
        long orderTotal,
        long expected)
    {
        var actual = RevenueRules.ExcessCollected(
            collected,
            orderTotal);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(100000L, 20000L, 80000L)]
    [InlineData(100000L, 0L, 100000L)]
    [InlineData(0L, 3000L, -3000L)]
    public void Net_cashflow_subtracts_successful_refunds(
        long collected,
        long successfulRefund,
        long expected)
    {
        var actual = RevenueRules.NetCashflow(
            collected,
            successfulRefund);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(10000L, 10000L)]
    [InlineData(12000L, 9000L)]
    [InlineData(7000L, 9000L)]
    [InlineData(0L, 9000L)]
    public void Valid_value_plus_excess_equals_collection(
        long collected,
        long orderTotal)
    {
        var valid = RevenueRules.ValidPaidOrderValue(
            collected,
            orderTotal);

        var excess = RevenueRules.ExcessCollected(
            collected,
            orderTotal);

        Assert.Equal(collected, valid + excess);
    }
}
