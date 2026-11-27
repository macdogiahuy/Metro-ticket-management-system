using Metro.Application.Abstractions.Network;
using Metro.Application.Network;

namespace Metro.UnitTests.Network;

public sealed class NetworkAdminRulesTests
{
    [Theory]
    [InlineData("active", "ACTIVE")]
    [InlineData(" PLANNED ", "PLANNED")]
    [InlineData("suspended", "SUSPENDED")]
    [InlineData("closed", "CLOSED")]
    public void NormalizeMasterStatus_accepts_valid_values(string input, string expected) =>
        Assert.Equal(expected, NetworkAdminRules.NormalizeMasterStatus(input));

    [Fact]
    public void NormalizeMasterStatus_rejects_invalid_value() =>
        Assert.Equal("INVALID_STATUS", Assert.Throws<NetworkAdminException>(() => NetworkAdminRules.NormalizeMasterStatus("DELETED")).Code);

    [Fact]
    public void ValidateLineStops_enforces_contiguous_sequence_unique_stations_monotonic_distance_and_non_negative_travel_time()
    {
        var s1 = Guid.NewGuid();
        var s2 = Guid.NewGuid();
        NetworkAdminRules.ValidateLineStops([
            new LineStopRuleInput(s1, 1, 0, 180),
            new LineStopRuleInput(s2, 2, 1500, 180)
        ]);

        Assert.Equal("INVALID_STOP_SEQUENCE", Assert.Throws<NetworkAdminException>(() =>
            NetworkAdminRules.ValidateLineStops([new LineStopRuleInput(s1, 2, 0, 180)])).Code);
        Assert.Equal("DUPLICATE_LINE_STATION", Assert.Throws<NetworkAdminException>(() =>
            NetworkAdminRules.ValidateLineStops([
                new LineStopRuleInput(s1, 1, 0, 180),
                new LineStopRuleInput(s1, 2, 1000, 180)
            ])).Code);
        Assert.Equal("INVALID_STOP_DISTANCE", Assert.Throws<NetworkAdminException>(() =>
            NetworkAdminRules.ValidateLineStops([
                new LineStopRuleInput(s1, 1, 1500, 180),
                new LineStopRuleInput(s2, 2, 1000, 180)
            ])).Code);
        Assert.Equal("INVALID_TRAVEL_TIME", Assert.Throws<NetworkAdminException>(() =>
            NetworkAdminRules.ValidateLineStops([new LineStopRuleInput(s1, 1, 0, -1)])).Code);
    }

    [Theory]
    [InlineData(6000.0)]
    [InlineData(18000.5)]
    public void ValidateMaxLoadKg_accepts_positive_one_decimal(decimal value) =>
        Assert.Equal(value, NetworkAdminRules.ValidateMaxLoadKg(value));

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    [InlineData(6000.12)]
    public void ValidateMaxLoadKg_rejects_non_positive_or_more_than_one_decimal(decimal value) =>
        Assert.Equal("INVALID_MAX_LOAD_KG", Assert.Throws<NetworkAdminException>(() => NetworkAdminRules.ValidateMaxLoadKg(value)).Code);

    [Theory]
    [InlineData(0.0)]
    [InlineData(17999.9)]
    [InlineData(18000.0)]
    [InlineData(19500.5)]
    public void ValidateObservedLoadKg_accepts_non_negative_one_decimal_including_overload(decimal value) =>
        Assert.Equal(value, NetworkAdminRules.ValidateObservedLoadKg(value));

    [Theory]
    [InlineData(-0.1)]
    [InlineData(17999.95)]
    public void ValidateObservedLoadKg_rejects_negative_or_two_decimals(decimal value) =>
        Assert.Equal("INVALID_LOAD_KG", Assert.Throws<NetworkAdminException>(() => NetworkAdminRules.ValidateObservedLoadKg(value)).Code);

    [Theory]
    [InlineData(null, 18000.0, "NO_DATA")]
    [InlineData(1000.0, null, "NO_LIMIT")]
    [InlineData(17999.9, 18000.0, "AVAILABLE")]
    [InlineData(18000.0, 18000.0, "FULL")]
    [InlineData(18000.1, 18000.0, "FULL")]
    public void ComputeLoadStatus_matches_AC_X18_boundary(double? observed, double? max, string expected) =>
        Assert.Equal(expected, NetworkAdminRules.ComputeLoadStatus(
            observed.HasValue ? (decimal)observed.Value : null,
            max.HasValue ? (decimal)max.Value : null));

    [Fact]
    public void ValidateManualEta_requires_timestamp_and_departure_not_before_arrival()
    {
        var now = DateTime.UtcNow;
        var (arr, dep) = NetworkAdminRules.ValidateManualEta(now.AddMinutes(5), now.AddMinutes(6));
        Assert.Equal(now.AddMinutes(5), arr);
        Assert.Equal(now.AddMinutes(6), dep);

        Assert.Equal("INVALID_TRIP_ETA", Assert.Throws<NetworkAdminException>(() => NetworkAdminRules.ValidateManualEta(null, null)).Code);
        Assert.Equal("INVALID_TRIP_ETA", Assert.Throws<NetworkAdminException>(() => NetworkAdminRules.ValidateManualEta(now.AddMinutes(6), now.AddMinutes(5))).Code);
    }

    [Fact]
    public void ValidateTripStatusTransition_enforces_ST009_and_cancellation_reason()
    {
        NetworkAdminRules.ValidateTripStatusTransition("PLANNED", "READY", null);
        NetworkAdminRules.ValidateTripStatusTransition("READY", "IN_SERVICE", null);
        NetworkAdminRules.ValidateTripStatusTransition("IN_SERVICE", "COMPLETED", null);
        NetworkAdminRules.ValidateTripStatusTransition("IN_SERVICE", "CANCELLED", "Signal fault");

        Assert.Equal("CANCELLATION_REASON_REQUIRED", Assert.Throws<NetworkAdminException>(() =>
            NetworkAdminRules.ValidateTripStatusTransition("PLANNED", "CANCELLED", "   ")).Code);
        Assert.Equal("INVALID_TRIP_TRANSITION", Assert.Throws<NetworkAdminException>(() =>
            NetworkAdminRules.ValidateTripStatusTransition("COMPLETED", "IN_SERVICE", null)).Code);
        Assert.Equal("INVALID_TRIP_TRANSITION", Assert.Throws<NetworkAdminException>(() =>
            NetworkAdminRules.ValidateTripStatusTransition("CANCELLED", "READY", null)).Code);
        Assert.Equal("INVALID_TRIP_TRANSITION", Assert.Throws<NetworkAdminException>(() =>
            NetworkAdminRules.ValidateTripStatusTransition("IN_SERVICE", "PLANNED", null)).Code);
    }

    [Fact]
    public void ValidateNoticePublishable_and_maker_checker_enforce_UC31_rules()
    {
        var now = DateTime.UtcNow;
        NetworkAdminRules.ValidateNoticePublishable(now.AddMinutes(-5), now.AddHours(1), now);
        Assert.Equal("NOTICE_EXPIRED", Assert.Throws<NetworkAdminException>(() =>
            NetworkAdminRules.ValidateNoticePublishable(now.AddHours(-2), now.AddMinutes(-1), now)).Code);

        Assert.Equal("MAKER_CHECKER_VIOLATION", Assert.Throws<NetworkAdminException>(() =>
            NetworkAdminRules.EnsureNoticeMakerChecker(false, "admin-1", "admin-1")).Code);
        // Emergency template allows creator fast-track per DB constraint.
        NetworkAdminRules.EnsureNoticeMakerChecker(true, "admin-1", "admin-1");
        NetworkAdminRules.EnsureNoticeMakerChecker(false, "admin-1", "admin-2");
    }

    [Fact]
    public void ValidateTripStopTimes_rejects_out_of_window_or_non_monotonic_times()
    {
        var start = new DateTime(2026, 11, 26, 5, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 11, 26, 5, 15, 0, DateTimeKind.Utc);
        var s1 = Guid.NewGuid();
        var s2 = Guid.NewGuid();

        NetworkAdminRules.ValidateTripStopTimes(start, end, [
            new TripStopTimeRuleInput(s1, 1, start, start.AddSeconds(30), 30),
            new TripStopTimeRuleInput(s2, 2, start.AddMinutes(3), start.AddMinutes(3).AddSeconds(30), 30),
        ]);

        Assert.Equal("STOP_OUTSIDE_TRIP_WINDOW", Assert.Throws<NetworkAdminException>(() =>
            NetworkAdminRules.ValidateTripStopTimes(start, end, [
                new TripStopTimeRuleInput(s1, 1, start.AddMinutes(-1), start, 0)
            ])).Code);
        Assert.Equal("NON_MONOTONIC_STOP_TIME", Assert.Throws<NetworkAdminException>(() =>
            NetworkAdminRules.ValidateTripStopTimes(start, end, [
                new TripStopTimeRuleInput(s1, 1, start.AddMinutes(5), start.AddMinutes(6), 60),
                new TripStopTimeRuleInput(s2, 2, start.AddMinutes(4), start.AddMinutes(5), 60)
            ])).Code);
    }

    [Fact]
    public void ValidateDaysOfWeek_and_Coordinates_validate_bounds()
    {
        Assert.Equal(new short[] { 1, 2, 5 }, NetworkAdminRules.ValidateDaysOfWeek([5, 1, 2, 1]));
        Assert.Equal("INVALID_DAYS_OF_WEEK", Assert.Throws<NetworkAdminException>(() => NetworkAdminRules.ValidateDaysOfWeek([0, 1])).Code);
        Assert.Equal("INVALID_DAYS_OF_WEEK", Assert.Throws<NetworkAdminException>(() => NetworkAdminRules.ValidateDaysOfWeek([8])).Code);
        Assert.Equal("INVALID_COORDINATES", Assert.Throws<NetworkAdminException>(() => NetworkAdminRules.ValidateCoordinates(91m, 105m)).Code);
        Assert.Equal("INVALID_COORDINATES", Assert.Throws<NetworkAdminException>(() => NetworkAdminRules.ValidateCoordinates(21m, 181m)).Code);
    }
}
