using System.Security.Cryptography;
using System.Text;
using Metro.Application.Abstractions.MultiTenancy;
using Metro.Application.Abstractions.Payment;
using Metro.Domain.Entities.Finance;
using Metro.Infrastructure.Persistence.Context;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using PaymentEntity = Metro.Domain.Entities.Finance.Payment;

namespace Metro.Infrastructure.Payments;

public sealed class PaymentService(
    ApplicationDbContext db,
    IOperatorContext operatorContext,
    IPaymentGateway gateway) : IPaymentService
{
    private const string Provider = "MOMO_TEST";

    public async Task<PaymentResponse> CreateAsync(CreatePaymentRequest request, string userId, CancellationToken cancellationToken)
    {
        var opId = await ResolveOperatorAsync(userId, cancellationToken);
        var order = await db.Orders.AsNoTracking()
            .SingleOrDefaultAsync(o => o.OperatorId == opId && o.Id == request.OrderId, cancellationToken)
            ?? throw new PaymentException("ORDER_NOT_FOUND", StatusCodes.Status404NotFound, "Order not found.");
        if (order.Status is not ("HELD" or "PENDING_PAYMENT"))
            throw new PaymentException("ORDER_NOT_CONFIRMABLE", StatusCodes.Status409Conflict, "Order is not awaiting payment.");

        var existing = await db.Payments.AsNoTracking().SingleOrDefaultAsync(
            p => p.OperatorId == opId && p.OrderId == order.Id && p.Status == PaymentStatus.Succeeded, cancellationToken);
        if (existing is not null) return ToResponse(existing, null);

        var idem = string.IsNullOrWhiteSpace(request.IdempotencyKey)
            ? $"pay-{Guid.NewGuid():N}" : request.IdempotencyKey.Trim();
        var paymentCode = $"PAY-{Guid.NewGuid():N}";
        var payment = new PaymentEntity
        {
            Id = Guid.NewGuid(), OperatorId = opId, OrderId = order.Id, PaymentCode = paymentCode,
            Provider = Provider, Method = PaymentMethod.SandboxGateway, AmountMinor = order.TotalMinor,
            Currency = order.Currency, Status = PaymentStatus.Pending, IdempotencyKey = idem,
            InitiatedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Payments.Add(payment);
        order.PaymentStartedAt = DateTime.UtcNow;
        db.Orders.Update(order);
        await db.SaveChangesAsync(cancellationToken);

        string? checkout = null;
        try
        {
            var (reference, url) = await gateway.CreateTransactionAsync(paymentCode, payment.AmountMinor, idem, cancellationToken);
            payment.ProviderPaymentReference = reference;
            checkout = url;
        }
        catch (PaymentException)
        {
            payment.Status = PaymentStatus.Failed;
            payment.FailureCode = "PROVIDER_ERROR";
            payment.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            throw;
        }
        payment.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return ToResponse(payment, checkout);
    }

    public async Task<PaymentResponse> GetAsync(Guid paymentId, string userId, CancellationToken cancellationToken)
    {
        var opId = await ResolveOperatorAsync(userId, cancellationToken);
        var payment = await db.Payments.AsNoTracking()
            .SingleOrDefaultAsync(p => p.OperatorId == opId && p.Id == paymentId, cancellationToken)
            ?? throw new PaymentException("PAYMENT_NOT_FOUND", StatusCodes.Status404NotFound, "Payment not found.");
        return ToResponse(payment, null);
    }

    public async Task<RetryPaymentResult> RetryAsync(Guid paymentId, string userId, CancellationToken cancellationToken)
    {
        var opId = await ResolveOperatorAsync(userId, cancellationToken);
        var payment = await db.Payments
            .SingleOrDefaultAsync(p => p.OperatorId == opId && p.Id == paymentId, cancellationToken)
            ?? throw new PaymentException("PAYMENT_NOT_FOUND", StatusCodes.Status404NotFound, "Payment not found.");
        if (payment.Status == PaymentStatus.Succeeded)
            return new RetryPaymentResult(ToResponse(payment, null), false);
        if (payment.Status is PaymentStatus.Refunded or PaymentStatus.PartiallyRefunded)
            throw new PaymentException("PAYMENT_RETRY_NOT_ALLOWED", StatusCodes.Status409Conflict, "Refunded payment cannot be retried.");

        // Fresh idempotency key: MoMo treats a repeated key as the same transaction.
        var idem = $"pay-retry-{Guid.NewGuid():N}";
        var (reference, checkout) = await gateway.CreateTransactionAsync(
            payment.PaymentCode, payment.AmountMinor, idem, cancellationToken);
        payment.Status = PaymentStatus.Pending;
        payment.IdempotencyKey = idem;
        payment.ProviderPaymentReference = reference;
        payment.FailureCode = null;
        payment.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return new RetryPaymentResult(ToResponse(payment, checkout), true);
    }

    public async Task<int> HandleWebhookAsync(PaymentCallback callback, CancellationToken cancellationToken)
    {
        // The webhook carries no JWT. Resolve the operator from the payment row (trusted DB),
        // never from a client-supplied parameter, then call the atomic claim+issue SQL function.
        var opId = await db.Payments.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.Id == callback.PaymentId)
            .Select(p => (Guid?)p.OperatorId)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new PaymentException("PAYMENT_NOT_FOUND", StatusCodes.Status404NotFound, "Payment not found.");
        operatorContext.CurrentOperatorId = opId;

        return await db.Database.SqlQueryRaw<int>(
            """
            SELECT metro.confirm_payment_and_issue_tickets({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}) AS "Value"
            """,
            opId,
            callback.PaymentId, callback.ProviderEventId, callback.EventIdempotencyKey,
            callback.PayloadHash, callback.AmountMinor, callback.Currency, true)
            .SingleAsync(cancellationToken);
    }

    private async Task<Guid> ResolveOperatorAsync(string userId, CancellationToken cancellationToken)
    {
        var id = await db.OperatorAccounts.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.UserId == userId && a.Status == "ACTIVE" && a.Operator.Status == "ACTIVE")
            .OrderBy(a => a.Id)
            .Select(a => (Guid?)a.OperatorId)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new PaymentException("OPERATOR_NOT_FOUND", StatusCodes.Status403Forbidden,
                "The account has no active operator membership.");
        operatorContext.CurrentOperatorId = id;
        return id;
    }

    private static PaymentResponse ToResponse(PaymentEntity payment, string? checkout) => new(
        payment.Id, payment.OrderId, payment.PaymentCode, payment.Provider, payment.Status.ToString().ToUpperInvariant(),
        payment.AmountMinor, payment.Currency, payment.ProviderPaymentReference, payment.IdempotencyKey,
        payment.InitiatedAt, checkout);
}
