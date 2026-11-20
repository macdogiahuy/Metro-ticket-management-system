using Metro.Application.Catalog;

namespace Metro.UnitTests.Catalog;

public sealed class CatalogStatusRulesTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("   ", null)]
    [InlineData("active", "ACTIVE")]
    [InlineData(" SUSPENDED ", "SUSPENDED")]
    [InlineData("Closed", "CLOSED")]
    [InlineData("PLANNED", "PLANNED")]
    [InlineData("UNKNOWN", "")]
    public void NormalizeAndValidateStatus_MapsExpectedValues(string? input, string? expected)
    {
        Assert.Equal(expected, CatalogStatusRules.NormalizeAndValidateStatus(input));
    }

    [Theory]
    [InlineData("ACTIVE", 0, "NORMAL")]
    [InlineData("ACTIVE", 2, "DISRUPTED")]
    [InlineData("SUSPENDED", 0, "SUSPENDED")]
    [InlineData("CLOSED", 1, "CLOSED")]
    [InlineData("PLANNED", 0, "PLANNED")]
    public void ComputeEntityOperationalState_AppliesStatusAndIncidentPrecedence(
        string status, int activeIncidents, string expected)
    {
        Assert.Equal(expected, CatalogStatusRules.ComputeEntityOperationalState(status, activeIncidents));
    }

    [Fact]
    public void ComputeOverallStatus_ReflectsDisruptedDegradedAndNormal()
    {
        Assert.Equal("DISRUPTED", CatalogStatusRules.ComputeOverallStatus(["NORMAL", "SUSPENDED"], ["NORMAL"], 0));
        Assert.Equal("DEGRADED", CatalogStatusRules.ComputeOverallStatus(["NORMAL"], ["NORMAL"], 1));
        Assert.Equal("NORMAL", CatalogStatusRules.ComputeOverallStatus(["NORMAL"], ["NORMAL"], 0));
    }

    [Fact]
    public void EvaluateManualEta_RejectsExpiredOrMissingEtaPerBr052()
    {
        var now = new DateTime(2026, 11, 19, 10, 0, 0, DateTimeKind.Utc);

        var future = CatalogStatusRules.EvaluateManualEta(now.AddMinutes(5), now.AddMinutes(6), now);
        Assert.Equal("UPDATED", future.EtaStatus);
        Assert.Equal(now.AddMinutes(5), future.Arrival);
        Assert.Equal(now.AddMinutes(6), future.Departure);

        var expired = CatalogStatusRules.EvaluateManualEta(now.AddMinutes(-1), now.AddMinutes(1), now);
        Assert.Equal("NO_ETA_UPDATE", expired.EtaStatus);
        Assert.Null(expired.Arrival);
        Assert.Null(expired.Departure);

        var empty = CatalogStatusRules.EvaluateManualEta(null, null, now);
        Assert.Equal("NO_ETA_UPDATE", empty.EtaStatus);
        Assert.Null(empty.Arrival);
        Assert.Null(empty.Departure);
    }
}
