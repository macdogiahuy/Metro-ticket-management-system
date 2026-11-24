using Metro.Application.Abstractions.Booking;
using Metro.Application.Abstractions.Catalog;
using Metro.Application.Abstractions.Fare;
using Metro.Application.Booking;
using Metro.Application.Fare;
using Metro.Application.Ticketing;
using Metro.Domain.Entities.Orders;
using Metro.Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace Metro.Infrastructure.Booking;

public sealed class BookingPricing(ApplicationDbContext db, ICatalogService catalog, IFareCalculator calculator)
{
    internal async Task<(TicketProduct Product, CheckoutPrice Price)> QuoteAsync(
        BookingSelection selection, string beneficiaryId, DateOnly birth, string operatorCode,
        DateTimeOffset now, DateOnly orderDate, CancellationToken ct)
    {
        var today = TicketRules.LocalDate(now);
        try
        {
            var group = FareRules.NormalizeEligibilityGroup(selection.EligibilityGroup);
            var child = TicketRules.IsChildUnderSix(birth, selection.ServiceDate ?? today);
            if (child != (group == "CHILD_UNDER_6"))
                throw new BookingException(child ? "CHILD_REQUIRES_FREE_SINGLE" : "CHILD_NOT_ELIGIBLE", 422);
            return await CalculateQuoteAsync(selection, group, beneficiaryId, birth, operatorCode, now, today, orderDate, ct);
        }
        catch (FareException ex)
        {
            throw new BookingException(ex.Code switch
            {
                "FARE_NOT_FOUND" => "BOOKING_FARE_UNAVAILABLE",
                "FARE_NOT_SELLABLE" or "FARE_OUT_OF_PAYMENT_RANGE" => "BOOKING_FARE_NOT_SELLABLE",
                _ => ex.Code
            }, ex.Code == "FARE_NOT_FOUND" ? 422 : ex.Status);
        }
        catch (OverflowException)
        {
            throw new BookingException("BOOKING_FARE_NOT_SELLABLE", 422);
        }
    }

    private async Task<(TicketProduct Product, CheckoutPrice Price)> CalculateQuoteAsync(BookingSelection selection,
        string group, string beneficiaryId, DateOnly birth, string operatorCode, DateTimeOffset now,
        DateOnly today, DateOnly orderDate, CancellationToken ct)
    {
        var product = await db.TicketProducts.AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == selection.TicketProductId && p.Status == "ACTIVE", ct)
            ?? throw new BookingException("BOOKING_PRODUCT_NOT_FOUND", 404);
        if (product.CapacityControlled || product.ProductType is not ("SINGLE_TRIP" or "DAY" or "WEEK" or "MONTH"))
            throw new BookingException("BOOKING_PRODUCT_UNSUPPORTED", 422);

        var single = product.ProductType == "SINGLE_TRIP";
        if (group == "CHILD_UNDER_6" && !single) throw new BookingException("CHILD_REQUIRES_FREE_SINGLE", 422);
        BookingRules.ValidateSelection(selection, single, today, orderDate);
        var origin = selection.OriginStationId;
        var destination = selection.DestinationStationId;
        JourneyOption? journey = null;
        if (single)
        {
            var routes = await catalog.SearchJourneysAsync(origin!.Value, destination!.Value, operatorCode, ct);
            journey = routes.Options.SingleOrDefault(x => x.OptionIndex == (selection.JourneyOptionIndex ?? 1))
                ?? throw new BookingException("BOOKING_ROUTE_UNAVAILABLE", 422);
        }

        var first = journey?.Legs[0];
        var line = journey?.Legs.Count == 1 ? first?.LineId : null;
        var utc = now.UtcDateTime;
        // Paid discounts apply to the ordinary fare. A separate matrix amount cannot replace the SRS rate.
        var rows = await (from entry in db.FareMatrixEntries.AsNoTracking()
            join policy in db.FarePolicyVersions.AsNoTracking()
                on new { entry.OperatorId, Id = entry.FarePolicyVersionId } equals new { policy.OperatorId, policy.Id }
            where entry.TicketProductId == product.Id && entry.EligibilityGroup == "ORDINARY" &&
                (entry.PaymentChannel == "ANY" || entry.PaymentChannel == "NON_CASH") &&
                policy.Status == "PUBLISHED" && policy.Currency == "VND" && policy.EffectiveFrom <= utc &&
                (policy.EffectiveTo == null || policy.EffectiveTo > utc) &&
                (entry.LineId == null && entry.OriginStationId == null && entry.DestinationStationId == null ||
                 single && (entry.LineId == null || entry.LineId == line) &&
                 (entry.OriginStationId == origin && entry.DestinationStationId == destination ||
                  entry.OriginStationId == destination && entry.DestinationStationId == origin))
            select new
            {
                PolicyId = policy.Id, policy.Version,
                Fare = new FareMatrixCandidate(entry.Id, entry.TicketProductId, entry.LineId, entry.OriginStationId,
                    entry.DestinationStationId, entry.PaymentChannel, entry.EligibilityGroup, entry.AmountMinor, entry.Sellable)
            }).ToListAsync(ct);
        if (rows.Count == 0) throw new BookingException("BOOKING_FARE_UNAVAILABLE", 422);
        if (rows.Select(row => row.PolicyId).Distinct().Skip(1).Any())
            throw new BookingException("BOOKING_FARE_SCOPE_AMBIGUOUS", 409);

        var referenceDate = selection.ServiceDate ?? today;
        // Evidence is valid at checkout/payment start. For a single ride the travel date is the separate age reference.
        var evidence = group == "ORDINARY" ? null : await db.EligibilityEvidence.AsNoTracking()
            .Where(e => e.UserId == beneficiaryId && e.Category == group && e.Status == "VERIFIED" &&
                e.ValidFrom <= today && e.ValidTo >= today && e.DecidedAt != null && e.DecidedByUserId != null)
            .OrderByDescending(e => e.DecidedAt).ThenBy(e => e.Id)
            .Select(e => new { e.Id, e.ValidFrom, e.ValidTo, e.DecidedAt })
            .FirstOrDefaultAsync(ct);
        var canApply = evidence is not null && (group != "ELDERLY" ||
            birth <= referenceDate && FareRules.CalculateAgeYears(birth, referenceDate) >= FareRules.ElderlyMinInclusiveAge);
        var freeChild = group == "CHILD_UNDER_6";
        var policies = canApply && !freeChild ? await LoadEligibilityPolicyAsync(group, product.Code, utc, ct) : [];
        var result = calculator.Calculate(new FareCalculationInput(product.Id, product.Code, product.ProductType,
            line, origin, destination, journey?.SegmentCount, "NON_CASH", freeChild ? "ORDINARY" : group, referenceDate,
            rows.Select(row => row.Fare).ToArray(), policies,
            new PassengerEligibilityContext(true, birth, evidence is not null, null, null)));
        var paidEvidence = result.AppliedEligibilityGroup == "ORDINARY" ? null : evidence;
        var snapshot = new CheckoutPrice(true, rows[0].PolicyId, rows[0].Version, result.MatchedMatrixEntryId!.Value,
            freeChild ? 0 : result.TotalAmountMinor, journey?.OptionIndex, journey?.SegmentCount, first?.LineId, first?.Direction, first?.ToStationId,
            journey is null ? null : string.Join("|", journey.Legs.Select(l =>
                $"{l.LineId:N}:{l.Direction}:{string.Join(",", l.Stations.Select(s => s.StationId.ToString("N")))}")),
            freeChild ? result.TotalAmountMinor : result.DiscountAmountMinor, group, freeChild ? group : result.AppliedEligibilityGroup,
            group == "ELDERLY" ? result.AgeReferenceDate : null, paidEvidence?.Id, paidEvidence?.ValidFrom,
            paidEvidence?.ValidTo, paidEvidence?.DecidedAt, result.AppliedEligibilityPolicyId);
        return (product, snapshot);
    }

    private async Task<EligibilityPolicyRule[]> LoadEligibilityPolicyAsync(string group, string productCode, DateTime utc, CancellationToken ct)
    {
        var policies = await db.EligibilityPolicies.AsNoTracking()
            .Where(p => p.BeneficiaryGroup == group && p.Status == "PUBLISHED" &&
                p.EffectiveFrom <= utc && (p.EffectiveTo == null || p.EffectiveTo > utc))
            .OrderByDescending(p => p.Version).ThenBy(p => p.Id)
            .Select(p => new EligibilityPolicyRule(p.Id, p.Code, p.Version, p.BeneficiaryGroup,
                p.DiscountType, p.DiscountValue, p.EligibleProductCodes))
            .ToArrayAsync(ct);
        var active = policies.FirstOrDefault(p => p.EligibleProductCodes.Count == 0 ||
            p.EligibleProductCodes.Contains(productCode, StringComparer.OrdinalIgnoreCase));
        if (active is null) return []; // BE-06 defaults to the SRS fixed adult rates.
        if (active.DiscountType != "PERCENT" || active.DiscountValue != (group == "STUDENT" ? 10m : 20m))
            throw new BookingException("BOOKING_ELIGIBILITY_POLICY_INVALID", 422);
        return [active];
    }
}

internal sealed record CheckoutPrice(bool BookingCheckout, Guid PolicyId, int PolicyVersion, Guid EntryId,
    long Amount, int? JourneyOptionIndex, int? SegmentCount, Guid? FirstLineId, short? FirstDirection, Guid? FirstLegEnd, string? Route,
    long Discount = 0, string RequestedGroup = "ORDINARY", string AppliedGroup = "ORDINARY", DateOnly? AgeReferenceDate = null,
    Guid? EvidenceId = null, DateOnly? EvidenceValidFrom = null, DateOnly? EvidenceValidTo = null,
    DateTime? EvidenceDecidedAt = null, Guid? EligibilityPolicyId = null)
{
    public long Subtotal => Amount + Discount;
}
