namespace Metro.Application.Abstractions.Payment;

public sealed record CreateRefundRequest(
    Guid PaymentId,
    Guid TicketId,
    string ReasonType,
    long RequestedAmountMinor,
    string? Reason = null,
    Guid? IncidentId = null
);

public sealed record ApproveRefundRequest(
    long? ApprovedAmountMinor = null,
    string? DecisionReason = null
);

public sealed record RefundResponse(
    Guid Id,
    Guid OperatorId,
    string RequestCode,
    Guid TicketId,
    Guid PaymentId,
    string ReasonType,
    long RequestedAmountMinor,
    long? ApprovedAmountMinor,
    string Status,
    string? SubmittedByUserId,
    string? ApprovedByUserId,
    DateTimeOffset? ApprovedAt,
    string? DecisionReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid? TransactionId = null,
    string? TransactionStatus = null
);

public sealed class RefundException(string code, int status, string message) : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
}

public interface IRefundService
{
    Task<RefundResponse> CreateAsync(CreateRefundRequest request, string userId, string? operatorCode, string idempotencyKey, CancellationToken ct);
    Task<RefundResponse> ApproveAsync(Guid refundRequestId, ApproveRefundRequest request, string adminUserId, string? operatorCode, string idempotencyKey, CancellationToken ct);
    Task<RefundResponse> GetAsync(Guid refundRequestId, string userId, string? operatorCode, CancellationToken ct);
}
