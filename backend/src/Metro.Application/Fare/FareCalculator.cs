using Metro.Application.Abstractions.Fare;

namespace Metro.Application.Fare;

/// <summary>Pure deterministic fare engine (UC09, BR-017, BR-019, BR-021, BR-029, BR-071) reusable by Checkout (BE-07).</summary>
public sealed class FareCalculator : IFareCalculator
{
    public FareCalculationResult Calculate(FareCalculationInput input)
    {
        var requestedGroup = FareRules.NormalizeEligibilityGroup(input.RequestedEligibilityGroup);
        var channel = FareRules.NormalizePaymentChannel(input.PaymentChannel);
        var productType = input.ProductType.Trim().ToUpperInvariant();

        if (productType == "SINGLE_TRIP" && (input.OriginStationId is null || input.DestinationStationId is null || input.SegmentCount is null or < 1))
            throw new FareException("JOURNEY_REQUIRED_FOR_SINGLE_TRIP", 400, "Origin, destination, and a valid serving route are required for SINGLE_TRIP fares.");

        if (requestedGroup == "CHILD_UNDER_6" && productType != "SINGLE_TRIP")
            throw new FareException("CHILD_SINGLE_TRIP_ONLY", 422, "CHILD_UNDER_6 free tickets apply only to SINGLE_TRIP products (BR-021).");

        var (appliedGroup, fallback, verifyReq, note) = EvaluateEligibilityStatus(input, requestedGroup);
        var map = BuildNullSafeIndex(input);
        string[] channels = channel == "ANY" ? ["ANY"] : [channel, "ANY"];

        // Check explicit group-specific matrix entry first (no double-discounting).
        if (appliedGroup != "ORDINARY" && TryResolveFare(map, input, productType, channels, appliedGroup, out var overrideMatch))
        {
            var baseMinor = TryResolveFare(map, input, productType, channels, "ORDINARY", out var ordMatch)
                ? Math.Max(ordMatch.AmountMinor, overrideMatch.AmountMinor) : overrideMatch.AmountMinor;
            var discountMinor = baseMinor - overrideMatch.AmountMinor;
            var isFree = overrideMatch.AmountMinor == 0 && appliedGroup == "CHILD_UNDER_6";
            FareRules.ValidatePaidRange(overrideMatch.AmountMinor, isFree);
            return new FareCalculationResult(baseMinor, discountMinor, overrideMatch.AmountMinor, "VND", requestedGroup,
                appliedGroup, overrideMatch.Entry.EntryId, null, overrideMatch.Source, input.ReferenceDate, isFree, fallback, verifyReq, note);
        }

        if (!TryResolveFare(map, input, productType, channels, "ORDINARY", out var baseMatch))
            throw new FareException("FARE_NOT_FOUND", 404, "No fare entry is configured for the selected product and journey in the active fare policy version.");

        var (policy, policyDiscount, isFreeTicket) = ApplyPolicyDiscount(input, appliedGroup, baseMatch.AmountMinor);
        var totalMinor = baseMatch.AmountMinor - policyDiscount;
        FareRules.ValidatePaidRange(totalMinor, isFreeTicket);

        return new FareCalculationResult(baseMatch.AmountMinor, policyDiscount, totalMinor, "VND", requestedGroup,
            appliedGroup, baseMatch.Entry.EntryId, policy?.Id, baseMatch.Source, input.ReferenceDate, isFreeTicket, fallback, verifyReq, note);
    }

    private static Dictionary<FareMatrixKey, FareMatrixCandidate> BuildNullSafeIndex(FareCalculationInput input)
    {
        var map = new Dictionary<FareMatrixKey, FareMatrixCandidate>();
        foreach (var e in input.MatrixEntries)
        {
            if (e.TicketProductId != input.TicketProductId) continue;
            var key = new FareMatrixKey(e.TicketProductId, e.LineId, e.OriginStationId, e.DestinationStationId,
                e.PaymentChannel.ToUpperInvariant(), e.EligibilityGroup.ToUpperInvariant());
            if (!map.TryAdd(key, e))
                throw new FareException("FARE_MATRIX_DUPLICATE", 409, "Ambiguous duplicate NULL-safe fare matrix key in fare policy version.");
        }
        return map;
    }

    private static bool TryResolveFare(
        Dictionary<FareMatrixKey, FareMatrixCandidate> map, FareCalculationInput input, string productType,
        string[] channels, string group, out (FareMatrixCandidate Entry, long AmountMinor, string Source) result)
    {
        if (productType == "SINGLE_TRIP")
        {
            var probes = new (Guid? LineId, Guid? Origin, Guid? Dest)[]
            {
                (input.LineId, input.OriginStationId, input.DestinationStationId),
                (null, input.OriginStationId, input.DestinationStationId),
                (input.LineId, input.DestinationStationId, input.OriginStationId),
                (null, input.DestinationStationId, input.OriginStationId),
            };
            foreach (var (lineId, orig, dest) in probes)
            {
                if (TryFind(map, input.TicketProductId, lineId, orig, dest, channels, group, out var od))
                {
                    EnsureSellable(od);
                    result = (od, od.AmountMinor, "FARE_MATRIX_OD");
                    return true;
                }
            }
            if (TryFind(map, input.TicketProductId, null, null, null, channels, group, out var formula))
            {
                EnsureSellable(formula);
                var amount = checked(formula.AmountMinor + (input.SegmentCount!.Value - 1L) * FareRules.SingleTripAdditionalSegmentMinor);
                result = (formula, amount, "SEGMENT_FORMULA");
                return true;
            }
        }
        else if (TryFind(map, input.TicketProductId, null, null, null, channels, group, out var flat))
        {
            EnsureSellable(flat);
            result = (flat, flat.AmountMinor, "FARE_MATRIX_FLAT");
            return true;
        }

        result = default;
        return false;
    }

    private static bool TryFind(Dictionary<FareMatrixKey, FareMatrixCandidate> map, Guid productId, Guid? lineId,
        Guid? originId, Guid? destId, string[] channels, string group, out FareMatrixCandidate entry)
    {
        foreach (var ch in channels)
            if (map.TryGetValue(new FareMatrixKey(productId, lineId, originId, destId, ch, group), out entry!))
                return true;
        entry = null!;
        return false;
    }

    private static void EnsureSellable(FareMatrixCandidate entry)
    {
        if (!entry.Sellable)
            throw new FareException("FARE_NOT_SELLABLE", 422, "The matched fare entry is currently marked as not sellable.");
    }

    private static (string AppliedGroup, bool Fallback, bool VerifyReq, string? Note) EvaluateEligibilityStatus(
        FareCalculationInput input, string requestedGroup)
    {
        if (requestedGroup == "ORDINARY") return ("ORDINARY", false, false, null);

        var p = input.Passenger;
        var hasValidApp = p is { HasApprovedApplication: true }
            && (p.ApplicationValidFrom is null || p.ApplicationValidFrom <= input.ReferenceDate)
            && (p.ApplicationValidTo is null || p.ApplicationValidTo >= input.ReferenceDate);

        if (requestedGroup == "CHILD_UNDER_6")
        {
            if (p?.BirthDate is { } childDob && FareRules.CalculateAgeYears(childDob, input.ReferenceDate) >= FareRules.ChildFreeMaxExclusiveAge)
                throw new FareException("CHILD_FREE_INELIGIBLE", 422, "Passenger is 6 or older on travel date (UTC+7); CHILD_UNDER_6 does not fall back to paid fare (BR-021).");
            if (p is { IsAuthenticated: true } && !hasValidApp)
                throw new FareException("CHILD_FREE_INELIGIBLE", 422, "Approved child/guardian eligibility profile is required for CHILD_UNDER_6 free fare (BR-021).");
            return ("CHILD_UNDER_6", false, p is not { IsAuthenticated: true },
                p is { IsAuthenticated: true } ? "Verified CHILD_UNDER_6 free single-trip fare." : "Reference CHILD_UNDER_6 free fare; verified guardian/age profile required at Checkout.");
        }

        if (requestedGroup == "ELDERLY" && p?.BirthDate is { } dob && FareRules.CalculateAgeYears(dob, input.ReferenceDate) < FareRules.ElderlyMinInclusiveAge)
            return ("ORDINARY", true, false, "Passenger is under 60 on reference date (UTC+7); ordinary fare applied (BR-071).");
        if (p is { IsAuthenticated: true } && !hasValidApp)
            return ("ORDINARY", true, false, $"Eligibility profile for {requestedGroup} is unverified or expired; ordinary fare applied (BR-019).");

        var verifyReq = p is not { IsAuthenticated: true };
        return (requestedGroup, false, verifyReq,
            verifyReq ? $"Reference {requestedGroup} fare; verified eligibility profile required at Checkout." : $"Verified {requestedGroup} eligibility applied.");
    }

    private static (EligibilityPolicyRule? Policy, long DiscountMinor, bool IsFree) ApplyPolicyDiscount(
        FareCalculationInput input, string appliedGroup, long baseMinor)
    {
        if (appliedGroup == "ORDINARY") return (null, 0, false);

        var policy = input.EligibilityPolicies
            .Where(x => x.BeneficiaryGroup.Equals(appliedGroup, StringComparison.OrdinalIgnoreCase)
                        && (x.EligibleProductCodes.Count == 0 || x.EligibleProductCodes.Contains(input.ProductCode, StringComparer.OrdinalIgnoreCase)))
            .OrderByDescending(x => x.Version).FirstOrDefault();

        var (type, value) = policy is not null
            ? (policy.DiscountType.ToUpperInvariant(), policy.DiscountValue)
            : appliedGroup switch { "CHILD_UNDER_6" => ("FREE", 100m), "STUDENT" => ("PERCENT", 10m), "ELDERLY" => ("PERCENT", 20m), _ => ("PERCENT", 0m) };

        var discount = type switch
        {
            "FREE" => baseMinor,
            "PERCENT" => FareRules.RoundHalfUp(baseMinor * (value / 100m)),
            "FIXED_AMOUNT" => FareRules.RoundHalfUp(value),
            _ => 0L,
        };
        discount = Math.Clamp(discount, 0L, baseMinor);
        return (policy, discount, type == "FREE" || (appliedGroup == "CHILD_UNDER_6" && discount == baseMinor));
    }
}
