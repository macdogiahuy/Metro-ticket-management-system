namespace Metro.Application.Abstractions.Fare;

public sealed record FareQuoteRequest(
    Guid? TicketProductId,
    string? ProductCode,
    Guid? OriginStationId,
    Guid? DestinationStationId,
    DateOnly? TravelDate,
    string? EligibilityGroup,
    string? PaymentChannel,
    DateOnly? BirthDate,
    Guid? FarePolicyVersionId,
    string? ScopeKey,
    string? OperatorCode);

public sealed record FarePolicySummary(
    Guid Id,
    string Code,
    int Version,
    string ScopeKey,
    string Status,
    DateTime EffectiveFrom,
    DateTime? EffectiveTo);

public sealed record TicketProductSummary(
    Guid Id,
    string Code,
    string Name,
    string ProductType,
    int? DurationValue,
    string? DurationUnit,
    string ExpiryRule);

public sealed record FareJourneySummary(
    Guid OriginStationId,
    string OriginStationCode,
    string OriginStationName,
    Guid DestinationStationId,
    string DestinationStationCode,
    string DestinationStationName,
    int SegmentCount,
    int TransferCount,
    int EstimatedDurationMinutes);

public sealed record FareQuoteResponse(
    string OperatorCode,
    DateTime QuotedAtUtc,
    bool IsReferenceQuote,
    FarePolicySummary FarePolicy,
    TicketProductSummary Product,
    FareJourneySummary? Journey,
    long BaseAmountMinor,
    long DiscountAmountMinor,
    long TotalAmountMinor,
    string Currency,
    string RequestedEligibilityGroup,
    string AppliedEligibilityGroup,
    Guid? MatchedMatrixEntryId,
    Guid? AppliedEligibilityPolicyId,
    string PricingSource,
    DateOnly AgeReferenceDate,
    bool IsFreeTicket,
    bool FallbackToOrdinary,
    bool VerificationRequired,
    string? EligibilityNote);

public interface IFareQuoteService
{
    Task<FareQuoteResponse> QuoteAsync(FareQuoteRequest request, string? authenticatedUserId, CancellationToken cancellationToken);
}
