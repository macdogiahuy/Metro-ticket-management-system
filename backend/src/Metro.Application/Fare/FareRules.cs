using Metro.Application.Abstractions.Fare;

namespace Metro.Application.Fare;

/// <summary>Pure SRS v1.2 fare, product-duration, eligibility, and UTC+7 age rules (BR-002, BR-003, BR-017, BR-019, BR-021, BR-029, BR-071).</summary>
public static class FareRules
{
    public const long MinPaidAmountMinor = 1_000;
    public const long MaxPaidAmountMinor = 50_000_000;
    public const long SingleTripAdditionalSegmentMinor = 1_000;
    public const int ChildFreeMaxExclusiveAge = 6;
    public const int ElderlyMinInclusiveAge = 60;

    public static readonly TimeZoneInfo VietnamTimeZone = ResolveVietnamTimeZone();

    public static readonly IReadOnlySet<string> SupportedProductTypes =
        new HashSet<string>(StringComparer.Ordinal) { "SINGLE_TRIP", "DAY", "WEEK", "MONTH" };

    public static readonly IReadOnlySet<string> SupportedEligibilityGroups =
        new HashSet<string>(StringComparer.Ordinal) { "ORDINARY", "STUDENT", "ELDERLY", "CHILD_UNDER_6" };

    public static readonly IReadOnlySet<string> SupportedPaymentChannels =
        new HashSet<string>(StringComparer.Ordinal) { "ANY", "NON_CASH", "CASH" };

    public static readonly IReadOnlySet<string> SupportedProductStatuses =
        new HashSet<string>(StringComparer.Ordinal) { "DRAFT", "ACTIVE", "SUSPENDED", "RETIRED" };

    public static DateOnly ToVietnamDate(DateTime utcTimestamp)
    {
        var utc = utcTimestamp.Kind == DateTimeKind.Utc ? utcTimestamp : utcTimestamp.ToUniversalTime();
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, VietnamTimeZone));
    }

    public static int CalculateAgeYears(DateOnly birthDate, DateOnly referenceDate)
    {
        if (birthDate > referenceDate)
            throw new FareException("INVALID_BIRTH_DATE", 400, "Birth date cannot be after the reference date.");
        var age = referenceDate.Year - birthDate.Year;
        if (referenceDate.Month < birthDate.Month || (referenceDate.Month == birthDate.Month && referenceDate.Day < birthDate.Day))
            age--;
        return age;
    }

    public static long RoundHalfUp(decimal amount) =>
        (long)Math.Round(amount, 0, MidpointRounding.AwayFromZero);

    public static string NormalizeEligibilityGroup(string? group)
    {
        var normalized = string.IsNullOrWhiteSpace(group) ? "ORDINARY" : group.Trim().ToUpperInvariant();
        if (!SupportedEligibilityGroups.Contains(normalized))
            throw new FareException("UNSUPPORTED_ELIGIBILITY_GROUP", 400,
                "Eligibility group must be one of: ORDINARY, STUDENT, ELDERLY, CHILD_UNDER_6.");
        return normalized;
    }

    public static string NormalizePaymentChannel(string? channel)
    {
        var normalized = string.IsNullOrWhiteSpace(channel) ? "ANY" : channel.Trim().ToUpperInvariant();
        if (!SupportedPaymentChannels.Contains(normalized))
            throw new FareException("INVALID_PAYMENT_CHANNEL", 400, "Payment channel must be one of: ANY, NON_CASH, CASH.");
        return normalized;
    }

    public static (string ProductType, int? DurationValue, string? DurationUnit, string ExpiryRule) ValidateProductSpec(
        string? productType, int? durationValue, string? durationUnit, string? expiryRule, bool capacityControlled)
    {
        var type = (productType ?? string.Empty).Trim().ToUpperInvariant();
        if (!SupportedProductTypes.Contains(type))
            throw new FareException("UNSUPPORTED_PRODUCT_TYPE", 400, "Product type must be one of: SINGLE_TRIP, DAY, WEEK, MONTH (no 3-day or multi-month passes).");
        if (capacityControlled)
            throw new FareException("CAPACITY_CONTROLLED_NOT_SUPPORTED", 400, "Legacy capacity-controlled products are not allowed under SRS v1.2.");

        var rule = (expiryRule ?? string.Empty).Trim().ToUpperInvariant();
        var unit = string.IsNullOrWhiteSpace(durationUnit) ? null : durationUnit.Trim().ToUpperInvariant();

        if (type == "SINGLE_TRIP")
        {
            if (durationValue is not null || unit is not null)
                throw new FareException("INVALID_SINGLE_TRIP_DURATION", 400, "SINGLE_TRIP products must not specify durationValue or durationUnit.");
            if (rule != "SINGLE_JOURNEY")
                throw new FareException("INVALID_EXPIRY_RULE", 400, "SINGLE_TRIP products must use expiryRule = SINGLE_JOURNEY.");
            return (type, null, null, rule);
        }

        if (durationValue is null or <= 0 || unit is null)
            throw new FareException("PRODUCT_DURATION_REQUIRED", 400, "Non-SINGLE_TRIP products must provide a positive durationValue and durationUnit.");
        if (unit is not ("DAY" or "MONTH"))
            throw new FareException("INVALID_DURATION_UNIT", 400, "durationUnit must be DAY or MONTH.");
        if (rule is not ("NEXT_LOCAL_MIDNIGHT" or "ELAPSED_DURATION"))
            throw new FareException("INVALID_EXPIRY_RULE", 400, "Periodic products must use expiryRule NEXT_LOCAL_MIDNIGHT or ELAPSED_DURATION.");

        var validDuration = type switch
        {
            "DAY" => durationValue == 1 && unit == "DAY",
            "WEEK" => durationValue == 7 && unit == "DAY",
            "MONTH" => (durationValue == 30 && unit == "DAY") || (durationValue == 1 && unit == "MONTH"),
            _ => false,
        };
        if (!validDuration)
            throw new FareException("UNSUPPORTED_PRODUCT_DURATION", 400,
                "Supported durations are DAY=1 DAY, WEEK=7 DAY, MONTH=30 DAY or 1 MONTH (3-day passes are prohibited).");
        return (type, durationValue, unit, rule);
    }

    public static void ValidatePaidRange(long totalMinor, bool isFreeTicket)
    {
        if (isFreeTicket && totalMinor == 0) return;
        if (totalMinor is < MinPaidAmountMinor or > MaxPaidAmountMinor)
            throw new FareException("FARE_OUT_OF_PAYMENT_RANGE", 422,
                $"Paid fare {totalMinor} VND is outside supported MoMo payment bounds ({MinPaidAmountMinor}..{MaxPaidAmountMinor} VND).");
    }

    private static TimeZoneInfo ResolveVietnamTimeZone()
    {
        foreach (var id in new[] { "Asia/Ho_Chi_Minh", "SE Asia Standard Time" })
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch (TimeZoneNotFoundException) { } catch (InvalidTimeZoneException) { }
        return TimeZoneInfo.CreateCustomTimeZone("UTC+07", TimeSpan.FromHours(7), "Vietnam Standard Time", "Vietnam Standard Time");
    }
}
