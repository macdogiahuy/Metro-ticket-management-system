using Metro.Application.Abstractions.MultiTenancy;
using Metro.Application.Abstractions.Payment;
using Metro.Application.Booking;
using Metro.Application.Finance;
using Metro.Domain.Entities.Finance;
using Metro.Infrastructure.Persistence.Context;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Metro.Infrastructure.Payments;

public sealed class RefundService(
    ApplicationDbContext db,
    IOperatorContext operatorContext) : IRefundService
{
    private const string Provider = "MOMO_TEST";

    public async Task<RefundResponse> CreateAsync(
        CreateRefundRequest request, string userId, string? operatorCode, string idempotencyKey, CancellationToken ct)
    {
        BookingRules.ValidateKey(idempotencyKey);
        RefundRules.ValidateReasonType(request.ReasonType);

        var opId = await ResolveOperatorAsync(operatorCode, request.PaymentId, userId, ct);

        // Fetch payment and tickets
        var payment = await db.Payments.SingleOrDefaultAsync(p => p.OperatorId == opId && p.Id == request.PaymentId, ct)
            ?? throw new RefundException("PAYMENT_NOT_FOUND", StatusCodes.Status404NotFound, "Payment not found.");

        RefundRules.ValidatePaymentEligibility(payment.Status.ToString());

        var ticket = await db.Tickets.SingleOrDefaultAsync(t => t.OperatorId == opId && t.Id == request.TicketId, ct)
            ?? throw new RefundException("TICKET_NOT_FOUND", StatusCodes.Status404NotFound, "Ticket not found.");

        // Verify ticket belongs to the same order as payment
        if (ticket.OrderId != payment.OrderId)
            throw new RefundException("TICKET_ORDER_MISMATCH", StatusCodes.Status400BadRequest, "Ticket does not belong to the payment order.");

        // Check already refunded amount on this payment
        var alreadyRefunded = await db.RefundTransactions
            .Where(r => r.OperatorId == opId && r.PaymentId == payment.Id && r.Status == RefundTransactionStatus.Succeeded)
            .SumAsync(r => (long?)r.AmountMinor, ct) ?? 0L;

        RefundRules.ValidateRequestedAmount(request.RequestedAmountMinor, payment.AmountMinor, alreadyRefunded);

        // Check if active refund request already exists for this ticket
        var existingActive = await db.RefundRequests
            .SingleOrDefaultAsync(r => r.OperatorId == opId && r.TicketId == ticket.Id &&
                (r.Status == RefundRequestStatus.Submitted ||
                 r.Status == RefundRequestStatus.UnderReview ||
                 r.Status == RefundRequestStatus.NeedsInfo ||
                 r.Status == RefundRequestStatus.PendingApproval ||
                 r.Status == RefundRequestStatus.Approved ||
                 r.Status == RefundRequestStatus.Processing), ct);

        if (existingActive is not null)
        {
            // Idempotent replay if caller and amount match
            if (existingActive.SubmittedByUserId == userId && existingActive.RequestedAmountMinor == request.RequestedAmountMinor)
                return ToResponse(existingActive);

            throw new RefundException("ACTIVE_REFUND_EXISTS", StatusCodes.Status409Conflict, "An active refund request already exists for this ticket.");
        }

        var now = DateTimeOffset.UtcNow;
        var refundRequest = new RefundRequest
        {
            Id = Guid.NewGuid(),
            OperatorId = opId,
            RequestCode = $"REF-REQ-{Guid.NewGuid():N}"[..28].ToUpperInvariant(),
            TicketId = ticket.Id,
            PaymentId = payment.Id,
            IncidentId = request.IncidentId,
            ReasonType = request.ReasonType.Trim().ToUpperInvariant(),
            RequestedAmountMinor = request.RequestedAmountMinor,
            Status = RefundRequestStatus.Submitted,
            SubmittedByUserId = userId,
            DecisionReason = request.Reason,
            CorrelationId = Guid.NewGuid(),
            CreatedAt = now,
            UpdatedAt = now
        };

        db.RefundRequests.Add(refundRequest);
        await db.SaveChangesAsync(ct);

        return ToResponse(refundRequest);
    }

    public async Task<RefundResponse> ApproveAsync(
        Guid refundRequestId, ApproveRefundRequest request, string adminUserId, string? operatorCode, string idempotencyKey, CancellationToken ct)
    {
        BookingRules.ValidateKey(idempotencyKey);
        RefundRules.ValidateDecisionReason(request.DecisionReason);

        var opId = await db.RefundRequests.IgnoreQueryFilters().AsNoTracking()
            .Where(r => r.Id == refundRequestId)
            .Select(r => (Guid?)r.OperatorId).SingleOrDefaultAsync(ct)
            ?? throw new RefundException("REFUND_REQUEST_NOT_FOUND", StatusCodes.Status404NotFound, "Refund request not found.");
        operatorContext.CurrentOperatorId = opId;

        var refundRequest = await db.RefundRequests
            .Include(r => r.Payment)
            .SingleOrDefaultAsync(r => r.OperatorId == opId && r.Id == refundRequestId, ct)
            ?? throw new RefundException("REFUND_REQUEST_NOT_FOUND", StatusCodes.Status404NotFound, "Refund request not found.");

        // Idempotency: if already approved or completed, return response cleanly
        if (refundRequest.Status is RefundRequestStatus.Approved or RefundRequestStatus.Completed)
        {
            var tx = await db.RefundTransactions.AsNoTracking()
                .SingleOrDefaultAsync(t => t.OperatorId == opId && t.RefundRequestId == refundRequest.Id, ct);
            return ToResponse(refundRequest, tx);
        }

        if (refundRequest.Status is not (RefundRequestStatus.Submitted or RefundRequestStatus.PendingApproval or RefundRequestStatus.UnderReview))
            throw new RefundException("REFUND_NOT_APPROVABLE", StatusCodes.Status409Conflict,
                $"Refund request is in state '{refundRequest.Status}' and cannot be approved.");

        var payment = refundRequest.Payment;

        var alreadyRefunded = await db.RefundTransactions
            .Where(r => r.OperatorId == opId && r.PaymentId == payment.Id && r.Status == RefundTransactionStatus.Succeeded)
            .SumAsync(r => (long?)r.AmountMinor, ct) ?? 0L;

        var remaining = payment.AmountMinor - alreadyRefunded;
        var approvedAmount = RefundRules.ValidateApprovalAmount(request.ApprovedAmountMinor, refundRequest.RequestedAmountMinor, remaining);

        var now = DateTimeOffset.UtcNow;

        refundRequest.ApprovedAmountMinor = approvedAmount;
        refundRequest.ApprovedByUserId = adminUserId;
        refundRequest.ApprovedAt = now;
        refundRequest.DecisionReason = request.DecisionReason!.Trim();
        refundRequest.Status = RefundRequestStatus.Completed;
        refundRequest.UpdatedAt = now;

        var refundTx = new RefundTransaction
        {
            Id = Guid.NewGuid(),
            OperatorId = opId,
            RefundRequestId = refundRequest.Id,
            PaymentId = payment.Id,
            RefundCode = $"REF-{Guid.NewGuid():N}"[..24].ToUpperInvariant(),
            Provider = Provider,
            Method = "SANDBOX_GATEWAY",
            AmountMinor = approvedAmount,
            Currency = payment.Currency,
            Status = RefundTransactionStatus.Succeeded,
            IdempotencyKey = idempotencyKey,
            PayoutRecipientSnapshot = "{}",
            ProcessedByUserId = adminUserId,
            SucceededAt = now,
            CorrelationId = Guid.NewGuid(),
            CreatedAt = now,
            UpdatedAt = now
        };

        db.RefundTransactions.Add(refundTx);

        // Update payment status without deleting payment record
        var newTotalRefunded = alreadyRefunded + approvedAmount;
        payment.Status = newTotalRefunded >= payment.AmountMinor
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;
        payment.UpdatedAt = now;

        await db.SaveChangesAsync(ct);

        // Revoke the ticket if eligible (so ticket cannot be used after refund)
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT metro.revoke_unused_ticket({opId}, {refundRequest.TicketId}, {refundRequest.DecisionReason})", ct);
        }
        catch
        {
            // If already revoked or cannot be directly revoked via SQL function, continue
        }

        return ToResponse(refundRequest, refundTx);
    }

    public async Task<RefundResponse> GetAsync(Guid refundRequestId, string userId, string? operatorCode, CancellationToken ct)
    {
        var opId = await db.RefundRequests.IgnoreQueryFilters().AsNoTracking()
            .Where(r => r.Id == refundRequestId)
            .Select(r => (Guid?)r.OperatorId).SingleOrDefaultAsync(ct)
            ?? throw new RefundException("REFUND_REQUEST_NOT_FOUND", StatusCodes.Status404NotFound, "Refund request not found.");
        operatorContext.CurrentOperatorId = opId;

        var refundRequest = await db.RefundRequests.AsNoTracking()
            .SingleOrDefaultAsync(r => r.OperatorId == opId && r.Id == refundRequestId, ct)
            ?? throw new RefundException("REFUND_REQUEST_NOT_FOUND", StatusCodes.Status404NotFound, "Refund request not found.");

        var tx = await db.RefundTransactions.AsNoTracking()
            .SingleOrDefaultAsync(t => t.OperatorId == opId && t.RefundRequestId == refundRequest.Id, ct);

        return ToResponse(refundRequest, tx);
    }

    private async Task<Guid> ResolveOperatorAsync(string? operatorCode, Guid? paymentId, string userId, CancellationToken ct)
    {
        if (operatorContext.CurrentOperatorId is { } existing && existing != Guid.Empty)
            return existing;

        if (!string.IsNullOrWhiteSpace(operatorCode))
        {
            var op = await db.Operators.AsNoTracking().Where(o => o.Code == operatorCode && o.Status == "ACTIVE")
                .Select(o => (Guid?)o.Id).SingleOrDefaultAsync(ct);
            if (op.HasValue)
            {
                operatorContext.CurrentOperatorId = op.Value;
                return op.Value;
            }
        }

        if (paymentId.HasValue)
        {
            var op = await db.Payments.IgnoreQueryFilters().AsNoTracking()
                .Where(p => p.Id == paymentId.Value)
                .Select(p => (Guid?)p.OperatorId).SingleOrDefaultAsync(ct);
            if (op.HasValue)
            {
                operatorContext.CurrentOperatorId = op.Value;
                return op.Value;
            }
        }

        var defaultOp = await db.Operators.AsNoTracking().Where(o => o.Status == "ACTIVE")
            .Select(o => o.Id).FirstOrDefaultAsync(ct);
        if (defaultOp != Guid.Empty)
        {
            operatorContext.CurrentOperatorId = defaultOp;
            return defaultOp;
        }

        throw new RefundException("OPERATOR_NOT_FOUND", 400, "Active operator not found.");
    }

    private static RefundResponse ToResponse(RefundRequest r, RefundTransaction? tx = null) => new(
        r.Id,
        r.OperatorId,
        r.RequestCode,
        r.TicketId,
        r.PaymentId,
        r.ReasonType,
        r.RequestedAmountMinor,
        r.ApprovedAmountMinor,
        r.Status.ToString().ToUpperInvariant(),
        r.SubmittedByUserId,
        r.ApprovedByUserId,
        r.ApprovedAt,
        r.DecisionReason,
        r.CreatedAt,
        r.UpdatedAt,
        tx?.Id,
        tx?.Status.ToString().ToUpperInvariant()
    );
}
