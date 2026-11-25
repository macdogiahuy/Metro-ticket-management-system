using Metro.Application.Abstractions.Payment;
using Metro.Application.Finance;

namespace Metro.UnitTests.Finance;

public sealed class RefundRulesTests
{
    [Theory]
    [InlineData("SERVICE_DISRUPTION")]
    [InlineData("SYSTEM_ERROR")]
    [InlineData("OVERPAYMENT")]
    [InlineData("OTHER")]
    [InlineData("service_disruption")]
    public void Valid_reason_types_are_accepted(string reason)
    {
        RefundRules.ValidateReasonType(reason);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("CUSTOMER_CHANGED_MIND")]
    [InlineData("INVALID_REASON")]
    public void Invalid_reason_types_throw_bad_request(string reason)
    {
        var ex = Assert.Throws<RefundException>(() => RefundRules.ValidateReasonType(reason));
        Assert.Equal(400, ex.Status);
        Assert.Equal("INVALID_REFUND_REASON_TYPE", ex.Code);
    }

    [Theory]
    [InlineData("SUCCEEDED")]
    [InlineData("PARTIALLY_REFUNDED")]
    [InlineData("succeeded")]
    public void Eligible_payment_status_is_accepted(string status)
    {
        RefundRules.ValidatePaymentEligibility(status);
    }

    [Theory]
    [InlineData("PENDING")]
    [InlineData("FAILED")]
    [InlineData("UNKNOWN")]
    [InlineData("REFUNDED")]
    public void Ineligible_payment_status_throws_conflict(string status)
    {
        var ex = Assert.Throws<RefundException>(() => RefundRules.ValidatePaymentEligibility(status));
        Assert.Equal(409, ex.Status);
        Assert.Equal("PAYMENT_NOT_REFUNDABLE", ex.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void Non_positive_refund_amount_throws_bad_request(long amount)
    {
        var ex = Assert.Throws<RefundException>(() => RefundRules.ValidateRequestedAmount(amount, 50000, 0));
        Assert.Equal(400, ex.Status);
        Assert.Equal("INVALID_REFUND_AMOUNT", ex.Code);
    }

    [Fact]
    public void Refund_amount_exceeding_remaining_throws_conflict()
    {
        var ex = Assert.Throws<RefundException>(() => RefundRules.ValidateRequestedAmount(30000, 50000, 25000));
        Assert.Equal(409, ex.Status);
        Assert.Equal("REFUND_AMOUNT_EXCEEDS_REMAINING", ex.Code);
    }

    [Fact]
    public void Valid_refund_amount_within_remaining_is_accepted()
    {
        RefundRules.ValidateRequestedAmount(25000, 50000, 25000);
        RefundRules.ValidateRequestedAmount(10000, 50000, 0);
    }

    [Fact]
    public void Approved_amount_cannot_exceed_requested()
    {
        var ex = Assert.Throws<RefundException>(() => RefundRules.ValidateApprovalAmount(60000, 50000, 100000));
        Assert.Equal(400, ex.Status);
        Assert.Equal("APPROVED_AMOUNT_EXCEEDS_REQUESTED", ex.Code);
    }

    [Fact]
    public void Approved_amount_defaults_to_requested_when_null()
    {
        var approved = RefundRules.ValidateApprovalAmount(null, 30000, 50000);
        Assert.Equal(30000, approved);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_decision_reason_throws_bad_request(string? reason)
    {
        var ex = Assert.Throws<RefundException>(() => RefundRules.ValidateDecisionReason(reason));
        Assert.Equal(400, ex.Status);
        Assert.Equal("DECISION_REASON_REQUIRED", ex.Code);
    }
}
