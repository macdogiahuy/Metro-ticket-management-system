using Metro.Application.Abstractions.Payment;

namespace Metro.Application.Finance;

public static class RefundRules
{
    private static readonly HashSet<string> AllowedReasonTypes =
    [
        "SERVICE_DISRUPTION",
        "SYSTEM_ERROR",
        "OVERPAYMENT",
        "OTHER"
    ];

    public static void ValidateReasonType(string reasonType)
    {
        if (string.IsNullOrWhiteSpace(reasonType) || !AllowedReasonTypes.Contains(reasonType.Trim().ToUpperInvariant()))
            throw new RefundException("INVALID_REFUND_REASON_TYPE", 400, $"Reason type '{reasonType}' is not supported.");
    }

    public static void ValidatePaymentEligibility(string paymentStatus)
    {
        var status = paymentStatus.Trim().ToUpperInvariant();
        if (status is not ("SUCCEEDED" or "PARTIALLY_REFUNDED"))
            throw new RefundException("PAYMENT_NOT_REFUNDABLE", 409, $"Payment with status '{paymentStatus}' cannot be refunded.");
    }

    public static void ValidateRequestedAmount(long requestedAmountMinor, long paymentAmountMinor, long alreadyRefundedMinor)
    {
        if (requestedAmountMinor <= 0)
            throw new RefundException("INVALID_REFUND_AMOUNT", 400, "Refund amount must be greater than zero.");

        var remainingRefundable = paymentAmountMinor - alreadyRefundedMinor;
        if (requestedAmountMinor > remainingRefundable)
            throw new RefundException("REFUND_AMOUNT_EXCEEDS_REMAINING", 409,
                $"Requested refund {requestedAmountMinor} exceeds remaining refundable amount {remainingRefundable}.");
    }

    public static long ValidateApprovalAmount(long? approvedAmountMinor, long requestedAmountMinor, long remainingRefundableMinor)
    {
        var approved = approvedAmountMinor ?? requestedAmountMinor;
        if (approved <= 0)
            throw new RefundException("INVALID_APPROVED_AMOUNT", 400, "Approved refund amount must be greater than zero.");

        if (approved > requestedAmountMinor)
            throw new RefundException("APPROVED_AMOUNT_EXCEEDS_REQUESTED", 400,
                $"Approved amount {approved} cannot exceed requested amount {requestedAmountMinor}.");

        if (approved > remainingRefundableMinor)
            throw new RefundException("APPROVED_AMOUNT_EXCEEDS_REMAINING", 409,
                $"Approved amount {approved} exceeds remaining refundable amount {remainingRefundableMinor}.");

        return approved;
    }

    public static void ValidateDecisionReason(string? decisionReason)
    {
        if (string.IsNullOrWhiteSpace(decisionReason))
            throw new RefundException("DECISION_REASON_REQUIRED", 400, "Decision reason is required for approving or rejecting a refund.");
    }
}
