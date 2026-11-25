namespace Metro.Application.Abstractions.Booking;

public sealed record BookingSelection(Guid TicketProductId, string? BeneficiaryUserId = null,
    DateOnly? ServiceDate = null, Guid? OriginStationId = null, Guid? DestinationStationId = null,
    int? JourneyOptionIndex = null, string EligibilityGroup = "ORDINARY", string? BeneficiaryEmail = null);

public sealed record ReviseBookingRequest(int OrderVersion, BookingSelection Selection);
public sealed record ConfirmBookingRequest(int OrderVersion, long ExpectedTotalMinor);
public sealed record BookingConsentRequest(int OrderVersion, Guid ConsentId, string Decision);
public sealed record CancelOrderRequest(int? OrderVersion = null, string? Reason = null);

public sealed record BookingResponse(Guid Id, string OrderCode, int OrderVersion, string Status,
    Guid TicketProductId, DateOnly? ServiceDate, Guid? OriginStationId, Guid? DestinationStationId,
    bool PurchaseForOther, bool IsBuyer, long TotalMinor, string Currency, Guid FarePolicyVersionId,
    DateTimeOffset PaymentStartDeadline, Guid? ConsentId, string? ConsentStatus, DateTimeOffset? ConsentExpiresAt,
    long DiscountMinor = 0, string RequestedEligibilityGroup = "ORDINARY", string AppliedEligibilityGroup = "ORDINARY",
    DateOnly? AgeReferenceDate = null, bool IsFreeChild = false, string? BuyerName = null, string? BeneficiaryName = null)
{
    public long SubtotalMinor => TotalMinor + DiscountMinor;
    public bool FallbackToOrdinary => RequestedEligibilityGroup != "ORDINARY" && AppliedEligibilityGroup == "ORDINARY";
}

public sealed record TripSuggestionResponse(int OrderVersion, string Status, Guid? PlannedTripId,
    Guid? LineId, short? Direction, DateTimeOffset? DepartureAt, string? Reason, string? Source,
    DateTimeOffset? ComputedAt, bool HoldsCapacity = false)
{
    public string Notice => "Gợi ý chuyến — không giữ chỗ";
}

public sealed class BookingException(string code, int status) : Exception(code)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
}
