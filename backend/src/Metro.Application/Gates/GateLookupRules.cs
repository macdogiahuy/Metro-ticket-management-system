using Metro.Application.Abstractions.Gates;

namespace Metro.Application.Gates;

/// <summary>BE-11 / UC19: a support lookup must identify a ticket by exactly one
/// of ticket code or passenger phone. Phone is reduced to national digits so
/// 0912..., +84 912... and 84912... all resolve to the same key. The raw phone
/// is never part of an exception message or a log line.</summary>
public static class GateLookupRules
{
    public static string NormalizePhone(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var digits = new string(value.Where(char.IsDigit).ToArray());
        if (digits.StartsWith("84", StringComparison.Ordinal)) digits = digits[2..];
        else if (digits.StartsWith('0')) digits = digits[1..];
        return digits;
    }

    public static void Validate(GateLookupRequest request)
    {
        var code = (request.TicketCode ?? string.Empty).Trim();
        var phone = NormalizePhone(request.Phone);
        var byCode = code.Length > 0;
        var byPhone = phone.Length > 0;

        if (byCode == byPhone ||
            (byCode && code.Length > 60) ||
            (byPhone && phone is not { Length: >= 9 and <= 10 }) ||
            string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 1000)
            throw new GateException("INVALID_GATE_LOOKUP", 400);
    }
}
