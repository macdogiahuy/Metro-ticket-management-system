using Metro.Application.Abstractions.Operations;
using Metro.Application.Operations;

namespace Metro.UnitTests.Operations;

public sealed class OperationsRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 12, 0, 0, TimeSpan.FromHours(7));

    [Theory]
    [InlineData("0", true)]
    [InlineData("17999.9", true)]
    [InlineData("18000", true)]
    [InlineData("18000.1", true)]
    [InlineData("-0.1", false)]
    [InlineData("1.01", false)]
    [InlineData("10000000", false)]
    public void Manual_kg_accepts_overload_but_never_negative_or_silent_precision_loss(string value, bool valid)
    {
        var kg = decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        var request = new RecordLoadRequest(kg, Now.AddMinutes(-1), "Manual observation");
        if (valid) OperationsRules.ValidateLoad(request, Now, new DateOnly(2026, 11, 24));
        else Assert.Equal("INVALID_LOAD_KG", Assert.Throws<OperationsException>(() =>
            OperationsRules.ValidateLoad(request, Now, new DateOnly(2026, 11, 24))).Code);
    }

    [Fact]
    public void Load_rejects_future_observations_and_different_service_day()
    {
        foreach (var stamp in new[] { Now.AddSeconds(1), Now.AddDays(-1) })
            Assert.Equal("INVALID_LOAD_OBSERVED_AT", Assert.Throws<OperationsException>(() => OperationsRules.ValidateLoad(
                new(0, stamp, "Observed"), Now, new(2026, 11, 24))).Code);
    }

    [Fact]
    public void ETA_accepts_manual_past_values_without_fabricating_freshness_but_rejects_wrong_day_and_order()
    {
        var station = Guid.NewGuid();
        OperationsRules.ValidateEta(new(station, Now.AddMinutes(-5), Now.AddMinutes(-4), "Late record"), new(2026, 11, 24));
        foreach (var request in new[] {
            new UpdateEtaRequest(station, null, null, "Missing"),
            new UpdateEtaRequest(station, Now, Now.AddSeconds(-1), "Invalid order"),
            new UpdateEtaRequest(station, Now.AddDays(1), null, "Wrong day") })
            Assert.Throws<OperationsException>(() => OperationsRules.ValidateEta(request, new(2026, 11, 24)));
    }

    [Theory]
    [InlineData("CANCELLED", "IN_SERVICE")]
    [InlineData("COMPLETED", "READY")]
    [InlineData("IN_SERVICE", "PLANNED")]
    [InlineData("DELAYED", "READY")]
    public void Trip_transitions_cannot_resurrect_or_rewind_service(string current, string target) =>
        Assert.Throws<OperationsException>(() => OperationsRules.ValidateTripTransition(current, target));

    [Theory]
    [InlineData("PLANNED", "READY")]
    [InlineData("IN_SERVICE", "DELAYED")]
    [InlineData("DISRUPTED", "IN_SERVICE")]
    [InlineData("IN_SERVICE", "CANCELLED")]
    public void Trip_transitions_accept_supported_operational_changes(string current, string target) =>
        Assert.Equal(target, OperationsRules.ValidateTripTransition(current, target));

    [Fact]
    public void Disruptions_have_exactly_one_scope_and_require_an_explicit_valid_effect()
    {
        var id = Guid.NewGuid();
        var request = new CreateDisruptionRequest("Delay", "Verified description", "DELAYED", Now, LineId: id);
        Assert.Equal("DELAYED", OperationsRules.ValidateDisruption(request, Now));
        Assert.Equal("CLOSED", OperationsRules.ValidateDisruption(request with { LineId = null, StationId = id, Effect = "CLOSED" }, Now));
        foreach (var invalid in new[] { request with { LineId = null }, request with { StationId = id },
            request with { Effect = "CLOSED" }, request with { OccurredAt = Now.AddSeconds(1) } })
            Assert.Throws<OperationsException>(() => OperationsRules.ValidateDisruption(invalid, Now));
    }

    [Fact]
    public void Idempotency_key_and_reason_are_bounded_and_nonempty()
    {
        OperationsRules.ValidateKey(new string('a', 120));
        Assert.Equal("reason", OperationsRules.RequireReason(" reason "));
        foreach (var key in new[] { "", "short", "with space", new string('x', 121) })
            Assert.Throws<OperationsException>(() => OperationsRules.ValidateKey(key));
        Assert.Throws<OperationsException>(() => OperationsRules.RequireReason(" "));
    }
}
