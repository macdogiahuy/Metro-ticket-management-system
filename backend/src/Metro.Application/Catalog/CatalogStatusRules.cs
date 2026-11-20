namespace Metro.Application.Catalog;

/// <summary>Pure catalog validation and UC10 status / BR-052 ETA freshness rules.</summary>
public static class CatalogStatusRules
{
    private static readonly HashSet<string> AllowedStatuses = new(StringComparer.Ordinal) { "PLANNED", "ACTIVE", "SUSPENDED", "CLOSED" };

    public static string? NormalizeAndValidateStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return null;
        var normalized = status.Trim().ToUpperInvariant();
        return AllowedStatuses.Contains(normalized) ? normalized : string.Empty;
    }

    public static string ComputeEntityOperationalState(string entityStatus, int activeIncidentCount) =>
        !entityStatus.Equals("ACTIVE", StringComparison.OrdinalIgnoreCase)
            ? entityStatus.ToUpperInvariant()
            : activeIncidentCount > 0 ? "DISRUPTED" : "NORMAL";

    public static string ComputeOverallStatus(IEnumerable<string> lineStates, IEnumerable<string> stationStates, int activeIncidentCount)
    {
        var all = lineStates.Concat(stationStates).ToArray();
        if (all.Any(s => s is "SUSPENDED" or "CLOSED")) return "DISRUPTED";
        return activeIncidentCount > 0 || all.Any(s => s == "DISRUPTED") ? "DEGRADED" : "NORMAL";
    }

    /// <summary>BR-052: Expired or missing manual ETA is never surfaced as a live prediction.</summary>
    public static (string EtaStatus, DateTime? Arrival, DateTime? Departure) EvaluateManualEta(
        DateTime? expectedArrivalAt, DateTime? expectedDepartureAt, DateTime utcNow) =>
        (expectedArrivalAt ?? expectedDepartureAt) is { } effective && effective >= utcNow
            ? ("UPDATED", expectedArrivalAt, expectedDepartureAt)
            : ("NO_ETA_UPDATE", null, null);
}
