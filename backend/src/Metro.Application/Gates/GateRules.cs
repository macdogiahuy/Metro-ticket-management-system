using Metro.Application.Abstractions.Gates;

namespace Metro.Application.Gates;

public static class GateRules
{
    public static void Key(Guid requestId, string key)
    {
        if (requestId == Guid.Empty || !Guid.TryParseExact(key, "D", out var id) || id != requestId)
            throw new GateException("GATE_REQUEST_ID_MISMATCH", 400);
    }

    public static void Validate(GateScanRequest request, string key)
    {
        Key(request.RequestId, key);
        if (request.Action is not ("ENTRY" or "EXIT") || request.StationId == Guid.Empty ||
            request.QrTokens is not { Length: >= 1 and <= 2 } ||
            request.QrTokens.Any(t => string.IsNullOrWhiteSpace(t) || t.Length > 512) ||
            request.QrTokens.Distinct(StringComparer.Ordinal).Count() != request.QrTokens.Length ||
            string.IsNullOrWhiteSpace(request.DeviceCode) || request.DeviceCode.Length > 80 || request.DeviceCode.Any(char.IsControl) ||
            request.Direction is not (null or 0 or 1) || request.PlannedTripId == Guid.Empty ||
            (request.Action == "ENTRY" && (request.PlannedTripId is null || request.Direction is null)))
            throw new GateException("INVALID_GATE_SCAN", 400);
    }

    public static void Validate(GateResolutionRequest request, string key)
    {
        Key(request.RequestId, key);
        if (request.SourceRequestId == Guid.Empty || request.SourceRequestId == request.RequestId || request.StationId == Guid.Empty ||
            request.Action is not ("CLOSE_JOURNEY" or "SUPPLY_ENTRY_AND_EXIT" or "UNDO_SCAN" or "ESCALATE" or "CLOSE_SUPPORT") ||
            string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 1000 ||
            string.IsNullOrWhiteSpace(request.EvidenceReference) || request.EvidenceReference.Length > 1000)
            throw new GateException("INVALID_GATE_RESOLUTION", 400);
        if (request.Action is not ("ESCALATE" or "CLOSE_SUPPORT") && !request.IdentityVerified)
            throw new GateException("GATE_IDENTITY_VERIFICATION_REQUIRED", 400);
        if (request.Action == "UNDO_SCAN" && !request.PassengerHasNotTravelled)
            throw new GateException("GATE_UNDO_REQUIRES_UNUSED_PASSAGE", 400);
        if (request.Action == "SUPPLY_ENTRY_AND_EXIT" && (request.ProvenEnteredAt is null || request.EntryStationId is null ||
            request.EntryStationId == Guid.Empty || request.EntryPlannedTripId is null || request.EntryPlannedTripId == Guid.Empty ||
            request.EntryDirection is not (0 or 1)))
            throw new GateException("GATE_ENTRY_EVIDENCE_REQUIRED", 400);
    }

    public static string? EntryLoad(decimal? observed, decimal? maximum) =>
        observed is null ? "LOAD_NO_DATA" : maximum is null or <= 0 ? "LOAD_NO_LIMIT" : observed >= maximum ? "LOAD_FULL" : null;
}
