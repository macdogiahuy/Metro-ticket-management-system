using Metro.Application.Abstractions.Catalog;
using Metro.Application.Abstractions.Fare;
using Metro.Application.Abstractions.MultiTenancy;
using Metro.Application.Fare;
using Metro.Domain.Entities.Orders;
using Metro.Infrastructure.Persistence.Context;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Metro.Infrastructure.Fare;

public sealed class FareQuoteService(
    ApplicationDbContext db,
    IOperatorContext operatorContext,
    IRouteFinder routeFinder,
    IFareCalculator calculator,
    ICatalogCache cache) : IFareQuoteService
{
    private static readonly TimeSpan MatrixCacheTtl = TimeSpan.FromSeconds(60);

    public async Task<FareQuoteResponse> QuoteAsync(
        FareQuoteRequest request, string? authenticatedUserId, CancellationToken cancellationToken)
    {
        var op = await ResolveActiveOperatorAsync(request.OperatorCode, cancellationToken);
        var product = await ResolveProductAsync(request.TicketProductId, request.ProductCode, cancellationToken);
        var nowUtc = DateTime.UtcNow;
        var todayUtc7 = FareRules.ToVietnamDate(nowUtc);
        var normalizedGroup = FareRules.NormalizeEligibilityGroup(request.EligibilityGroup);
        var channel = FareRules.NormalizePaymentChannel(string.IsNullOrWhiteSpace(request.PaymentChannel) ? "NON_CASH" : request.PaymentChannel);
        var farePolicy = await ResolveFarePolicyVersionAsync(request.FarePolicyVersionId, request.ScopeKey, nowUtc, cancellationToken);

        Guid? singleLineId = null;
        int? segmentCount = null;
        FareJourneySummary? journey = null;
        DateOnly referenceDate;

        if (product.ProductType == "SINGLE_TRIP")
        {
            if (request.OriginStationId is null || request.DestinationStationId is null)
                throw new FareException("JOURNEY_REQUIRED_FOR_SINGLE_TRIP", StatusCodes.Status400BadRequest,
                    "originStationId and destinationStationId are required for SINGLE_TRIP quotes.");
            if (request.OriginStationId == request.DestinationStationId)
                throw new FareException("SAME_STATION", StatusCodes.Status400BadRequest,
                    "Origin and destination stations must be different.");

            referenceDate = request.TravelDate ?? todayUtc7;
            if (referenceDate < todayUtc7)
                throw new FareException("INVALID_TRAVEL_DATE", StatusCodes.Status400BadRequest,
                    "travelDate cannot be in the past (UTC+7).");

            var stations = await db.Stations.AsNoTracking()
                .Select(s => new GraphStation(s.Id, s.Code, s.Name, s.Status, s.MinTransferSeconds)).ToListAsync(cancellationToken);
            var origin = stations.FirstOrDefault(s => s.Id == request.OriginStationId.Value);
            var dest = stations.FirstOrDefault(s => s.Id == request.DestinationStationId.Value);
            if (origin is null || dest is null)
                throw new FareException("STATION_NOT_FOUND", StatusCodes.Status404NotFound, "Origin or destination station was not found.");

            var lines = await db.Lines.AsNoTracking().Select(l => new GraphLine(l.Id, l.Code, l.Name, l.Status)).ToListAsync(cancellationToken);
            var stops = await db.LineStations.AsNoTracking()
                .Select(ls => new GraphLineStop(ls.LineId, ls.StationId, ls.Direction, ls.SequenceNo, ls.TravelTimeSeconds, ls.DistanceFromOriginM))
                .ToListAsync(cancellationToken);

            var options = routeFinder.FindOptimalRoutes(new NetworkGraphData(lines, stations, stops), origin.Id, dest.Id);
            if (options.Count == 0)
                throw new FareException("PATH_UNAVAILABLE", StatusCodes.Status422UnprocessableEntity,
                    "No serving metro route is currently available between the selected stations (BR-051).");

            var best = options[0];
            singleLineId = best.Legs.Count == 1 ? best.Legs[0].LineId : null;
            segmentCount = best.SegmentCount;
            journey = new FareJourneySummary(origin.Id, origin.Code, origin.Name, dest.Id, dest.Code, dest.Name,
                best.SegmentCount, best.TransferCount, best.EstimatedDurationMinutes);
        }
        else
        {
            // BR-071: DAY/WEEK/MONTH age reference date is PaymentStart / today in UTC+7.
            referenceDate = todayUtc7;
        }

        var matrixEntries = await LoadMatrixEntriesAsync(op.Id, farePolicy, product.Id, cancellationToken);
        var policies = await db.EligibilityPolicies.AsNoTracking()
            .Where(p => p.Status == "PUBLISHED" && p.EffectiveFrom <= nowUtc && (p.EffectiveTo == null || p.EffectiveTo > nowUtc))
            .Select(p => new EligibilityPolicyRule(p.Id, p.Code, p.Version, p.BeneficiaryGroup, p.DiscountType, p.DiscountValue, p.EligibleProductCodes))
            .ToListAsync(cancellationToken);

        var passenger = await LoadPassengerEligibilityAsync(authenticatedUserId, normalizedGroup, request.BirthDate, cancellationToken);
        var calc = calculator.Calculate(new FareCalculationInput(
            product.Id, product.Code, product.ProductType, singleLineId,
            request.OriginStationId, request.DestinationStationId, segmentCount,
            channel, normalizedGroup, referenceDate, matrixEntries, policies, passenger));

        return new FareQuoteResponse(
            op.Code, nowUtc, IsReferenceQuote: true,
            new FarePolicySummary(farePolicy.Id, farePolicy.Code, farePolicy.Version, farePolicy.ScopeKey, farePolicy.Status, farePolicy.EffectiveFrom, farePolicy.EffectiveTo),
            new TicketProductSummary(product.Id, product.Code, product.Name, product.ProductType, product.DurationValue, product.DurationUnit, product.ExpiryRule),
            journey, calc.BaseAmountMinor, calc.DiscountAmountMinor, calc.TotalAmountMinor, calc.Currency,
            calc.RequestedEligibilityGroup, calc.AppliedEligibilityGroup, calc.MatchedMatrixEntryId, calc.AppliedEligibilityPolicyId,
            calc.PricingSource, calc.AgeReferenceDate, calc.IsFreeTicket, calc.FallbackToOrdinary, calc.VerificationRequired, calc.EligibilityNote);
    }

    private async Task<IReadOnlyList<FareMatrixCandidate>> LoadMatrixEntriesAsync(
        Guid operatorId, FarePolicyVersion version, Guid productId, CancellationToken ct)
    {
        Task<List<FareMatrixCandidate>> QueryAsync(CancellationToken token) =>
            db.FareMatrixEntries.AsNoTracking()
                .Where(e => e.FarePolicyVersionId == version.Id && e.TicketProductId == productId)
                .Select(e => new FareMatrixCandidate(e.Id, e.TicketProductId, e.LineId, e.OriginStationId, e.DestinationStationId,
                    e.PaymentChannel, e.EligibilityGroup, e.AmountMinor, e.Sellable))
                .ToListAsync(token);

        // Cache only immutable PUBLISHED versions keyed by versionId so new publishes take effect immediately.
        if (version.Status != "PUBLISHED") return await QueryAsync(ct);
        return await cache.GetOrCreateAsync($"fare-matrix:{operatorId:N}:{version.Id:N}:{productId:N}", MatrixCacheTtl, QueryAsync, ct);
    }

    private async Task<PassengerEligibilityContext> LoadPassengerEligibilityAsync(
        string? userId, string group, DateOnly? guestBirthDate, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return new PassengerEligibilityContext(false, guestBirthDate, false, null, null);

        var birthDate = await db.UserProfiles.AsNoTracking()
            .Where(u => u.UserId == userId).Select(u => (DateOnly?)u.BirthDate).SingleOrDefaultAsync(ct);
        if (group == "ORDINARY")
            return new PassengerEligibilityContext(true, birthDate, false, null, null);

        var approved = await db.EligibilityApplications.AsNoTracking()
            .Where(a => a.ApplicantUserId == userId && a.Status == "APPROVED" && a.EligibilityPolicy.BeneficiaryGroup == group)
            .OrderByDescending(a => a.DecidedAt).ThenByDescending(a => a.CreatedAt)
            .Select(a => new { a.ValidFrom, a.ValidTo }).FirstOrDefaultAsync(ct);

        return new PassengerEligibilityContext(true, birthDate, approved is not null, approved?.ValidFrom, approved?.ValidTo);
    }

    private async Task<TicketProduct> ResolveProductAsync(Guid? productId, string? productCode, CancellationToken ct)
    {
        if (productId is null && string.IsNullOrWhiteSpace(productCode))
            throw new FareException("PRODUCT_REQUIRED", StatusCodes.Status400BadRequest, "Either ticketProductId or productCode is required.");

        var query = db.TicketProducts.AsNoTracking();
        var product = productId.HasValue
            ? await query.SingleOrDefaultAsync(p => p.Id == productId.Value, ct)
            : await query.SingleOrDefaultAsync(p => p.Code == productCode!.Trim(), ct);

        if (product is null)
            throw new FareException("PRODUCT_NOT_FOUND", StatusCodes.Status404NotFound, "Ticket product was not found.");
        if (product.Status != "ACTIVE")
            throw new FareException("PRODUCT_NOT_ACTIVE", StatusCodes.Status422UnprocessableEntity, "Ticket product is not currently active.");
        return product;
    }

    private async Task<FarePolicyVersion> ResolveFarePolicyVersionAsync(
        Guid? versionId, string? scopeKey, DateTime nowUtc, CancellationToken ct)
    {
        if (versionId.HasValue)
        {
            return await db.FarePolicyVersions.AsNoTracking().SingleOrDefaultAsync(v => v.Id == versionId.Value, ct)
                ?? throw new FareException("FARE_POLICY_NOT_FOUND", StatusCodes.Status404NotFound, "Fare policy version was not found.");
        }

        var query = db.FarePolicyVersions.AsNoTracking()
            .Where(v => v.Status == "PUBLISHED" && v.EffectiveFrom <= nowUtc && (v.EffectiveTo == null || v.EffectiveTo > nowUtc));
        if (!string.IsNullOrWhiteSpace(scopeKey))
        {
            var normalizedScope = scopeKey.Trim();
            query = query.Where(v => v.ScopeKey == normalizedScope);
        }

        return await query.OrderByDescending(v => v.Version).ThenByDescending(v => v.EffectiveFrom).FirstOrDefaultAsync(ct)
            ?? throw new FareException("FARE_POLICY_NOT_FOUND", StatusCodes.Status404NotFound, "No active published fare policy version is configured.");
    }

    private async Task<(Guid Id, string Code)> ResolveActiveOperatorAsync(string? operatorCode, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(operatorCode))
        {
            var code = operatorCode.Trim();
            var matched = await db.Operators.AsNoTracking().Where(o => o.Code == code && o.Status == "ACTIVE")
                .Select(o => new { o.Id, o.Code }).SingleOrDefaultAsync(ct)
                ?? throw new FareException("OPERATOR_NOT_FOUND", StatusCodes.Status404NotFound, "Operator not found.");
            operatorContext.CurrentOperatorId = matched.Id;
            return (matched.Id, matched.Code);
        }
        var defaultOp = await db.Operators.AsNoTracking().Where(o => o.Status == "ACTIVE")
            .OrderBy(o => o.CreatedAt).ThenBy(o => o.Id).Select(o => new { o.Id, o.Code }).FirstOrDefaultAsync(ct)
            ?? throw new FareException("OPERATOR_NOT_FOUND", StatusCodes.Status404NotFound, "No active operator is configured.");
        operatorContext.CurrentOperatorId = defaultOp.Id;
        return (defaultOp.Id, defaultOp.Code);
    }
}
