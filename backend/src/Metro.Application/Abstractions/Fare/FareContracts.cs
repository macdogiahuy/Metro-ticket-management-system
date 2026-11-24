namespace Metro.Application.Abstractions.Fare;

/// <summary>NULL-safe natural key matching metro.fare_matrix_entries UNIQUE NULLS NOT DISTINCT.</summary>
public readonly record struct FareMatrixKey(
    Guid TicketProductId,
    Guid? LineId,
    Guid? OriginStationId,
    Guid? DestinationStationId,
    string PaymentChannel,
    string EligibilityGroup);

public sealed record FareMatrixCandidate(
    Guid EntryId,
    Guid TicketProductId,
    Guid? LineId,
    Guid? OriginStationId,
    Guid? DestinationStationId,
    string PaymentChannel,
    string EligibilityGroup,
    long AmountMinor,
    bool Sellable)
{
    public FareMatrixKey ToKey() => new(TicketProductId, LineId, OriginStationId, DestinationStationId, PaymentChannel, EligibilityGroup);
}

public sealed record EligibilityPolicyRule(
    Guid Id,
    string Code,
    int Version,
    string BeneficiaryGroup,
    string DiscountType,
    decimal DiscountValue,
    IReadOnlyList<string> EligibleProductCodes);

public sealed record PassengerEligibilityContext(
    bool IsAuthenticated,
    DateOnly? BirthDate,
    bool HasApprovedApplication,
    DateOnly? ApplicationValidFrom,
    DateOnly? ApplicationValidTo);

public sealed record FareCalculationInput(
    Guid TicketProductId,
    string ProductCode,
    string ProductType,
    Guid? LineId,
    Guid? OriginStationId,
    Guid? DestinationStationId,
    int? SegmentCount,
    string PaymentChannel,
    string RequestedEligibilityGroup,
    DateOnly ReferenceDate,
    IReadOnlyList<FareMatrixCandidate> MatrixEntries,
    IReadOnlyList<EligibilityPolicyRule> EligibilityPolicies,
    PassengerEligibilityContext? Passenger = null);

public sealed record FareCalculationResult(
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

public interface IFareCalculator
{
    FareCalculationResult Calculate(FareCalculationInput input);
}

/// <summary>Stable machine-readable error for ProblemDetails extension "code".</summary>
public sealed class FareException(string code, int status, string message) : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
}
