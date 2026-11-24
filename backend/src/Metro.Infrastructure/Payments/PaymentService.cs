using System.Security.Cryptography;
using System.Text;
using Metro.Application.Abstractions.MultiTenancy;
using Metro.Application.Abstractions.Payment;
using Metro.Application.Booking;
using Metro.Domain.Entities.Finance;
using Metro.Domain.Entities.Orders;
using Metro.Infrastructure.Booking;
using Metro.Infrastructure.Persistence.Context;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PaymentEntity = Metro.Domain.Entities.Finance.Payment;

namespace Metro.Infrastructure.Payments;

public sealed class PaymentService(
    ApplicationDbContext db,
    IOperatorContext operatorContext,
    IPaymentGateway gateway,
    BookingService booking) : IPaymentService
{
    private const string Provider = "MOMO_TEST";

    public async Task<PaymentResponse> CreateAsync(CreatePaymentRequest request, string userId, CancellationToken cancellationToken)
    {
        var opId = await ResolveOrderOperatorAsync(request.OrderId, userId, cancellationToken);
        var order = await db.Orders.AsNoTracking()
            .SingleOrDefaultAsync(o => o.OperatorId == opId && o.Id == request.OrderId && o.PayerUserId == userId, cancellationToken)
            ?? throw new PaymentException("ORDER_NOT_FOUND", StatusCodes.Status404NotFound, "Order not found.");
        var managed = BookingService.IsBooking(order.FareSnapshot);
        if (!managed && order.Status is not ("HELD" or "PENDING_PAYMENT"))
            throw new PaymentException("ORDER_NOT_CONFIRMABLE", StatusCodes.Status409Conflict, "Order is not awaiting payment.");

        var existing = await db.Payments.AsNoTracking().SingleOrDefaultAsync(
            p => p.OperatorId == opId && p.OrderId == order.Id && p.Status == PaymentStatus.Succeeded, cancellationToken);
        if (existing is not null && !managed) return ToResponse(existing, null);

        if (managed && string.IsNullOrWhiteSpace(request.IdempotencyKey))
            throw new PaymentException("PAYMENT_IDEMPOTENCY_KEY_REQUIRED", 400, "Provide a payment request key.");
        if (managed) BookingRules.ValidateKey(request.IdempotencyKey!);
        var idem = string.IsNullOrWhiteSpace(request.IdempotencyKey)
            ? $"pay-{Guid.NewGuid():N}" : request.IdempotencyKey.Trim();
        var prepared = managed
            ? await PrepareBookingPaymentAsync(order, request.OrderVersion, idem, cancellationToken)
            : (await SavePaymentAsync(order, idem, cancellationToken), true);
        var (payment, created) = prepared;
        if (!created) return ToResponse(payment, payment.CheckoutUrl);

        string? checkout = null;
        try
        {
            var (reference, url) = await gateway.CreateTransactionAsync(payment.PaymentCode, payment.AmountMinor, idem, cancellationToken);
            payment.ProviderPaymentReference = reference;
            checkout = url;
            payment.CheckoutUrl = url;
        }
        catch (PaymentException)
        {
            payment.Status = PaymentStatus.Failed;
            payment.FailureCode = "PROVIDER_ERROR";
            payment.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // The pending attempt was committed before contacting the gateway. A transport
            // failure cannot prove no charge and must never reopen Checkout or start a new attempt.
            throw new PaymentException("PAYMENT_OUTCOME_UNKNOWN", 503, "Payment outcome is not yet known. Reconciliation is required before another attempt.");
        }
        payment.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return ToResponse(payment, checkout);
    }

    private Task<(PaymentEntity Payment, bool Created)> PrepareBookingPaymentAsync(Order initial, int? expectedVersion, string key, CancellationToken ct) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(async _ =>
        {
            db.ChangeTracker.Clear();
            BookingRules.ValidateKey(key);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var requestLock = $"payment:{initial.OperatorId}:{Provider}:{key}";
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({requestLock},0))", ct);
            var replay = await db.Payments.SingleOrDefaultAsync(p => p.Provider == Provider && p.IdempotencyKey == key, ct);
            if (replay is not null)
            {
                if (replay.OrderId != initial.Id || replay.OrderVersion != expectedVersion)
                    throw new PaymentException("PAYMENT_IDEMPOTENCY_CONFLICT", 409, "The request key belongs to different payment content.");
                return (replay, false);
            }
            var beneficiaryLock = $"{initial.OperatorId}:{initial.BeneficiaryUserId}";
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({beneficiaryLock},0))", ct);
            var order = await booking.LockAsync(initial.Id, ct);
            if (expectedVersion is null || order.OrderVersion != expectedVersion || order.BeneficiaryUserId != initial.BeneficiaryUserId)
                throw new PaymentException("ORDER_VERSION_CONFLICT", 409, "Booking changed before payment started.");
            if (order.TotalMinor == 0)
                throw new PaymentException("FREE_ORDER_HAS_NO_PAYMENT", 409, "Confirm the free child order at Checkout.");
            var attemptCount = await db.Payments.CountAsync(p => p.OrderId == order.Id, ct);
            if (attemptCount >= 3) throw new PaymentException("PAYMENT_ATTEMPT_LIMIT", 409, "An order permits at most three attempts.");
            var paymentStart = await booking.ValidatePaymentStartAsync(order, ct);
            var payment = await SavePaymentAsync(order, key, ct, paymentStart);
            await tx.CommitAsync(ct);
            return (payment, true);
        }, ct);

    private async Task<PaymentEntity> SavePaymentAsync(Order order, string key, CancellationToken ct, DateTimeOffset? paymentStart = null)
    {
        var now = paymentStart ?? DateTimeOffset.UtcNow;
        // PostgreSQL stores microseconds. Return the same timestamp before and after an idempotent replay.
        now = now.AddTicks(-(now.Ticks % 10));
        var payment = new PaymentEntity
        {
            Id = Guid.NewGuid(), OperatorId = order.OperatorId, OrderId = order.Id, PaymentCode = $"PAY-{Guid.NewGuid():N}",
            Provider = Provider, Method = PaymentMethod.SandboxGateway, AmountMinor = order.TotalMinor,
            Currency = order.Currency, Status = PaymentStatus.Pending, IdempotencyKey = key,
            InitiatedAt = now, CreatedAt = now, UpdatedAt = now,
            AttemptNumber = paymentStart is null ? null : await db.Payments.CountAsync(p => p.OrderId == order.Id, ct) + 1,
        };
        db.Payments.Add(payment);
        if (db.Entry(order).State == EntityState.Detached) db.Orders.Attach(order);
        order.PaymentStartedAt = now.UtcDateTime;
        await db.SaveChangesAsync(ct);
        return payment;
    }

    public async Task<PaymentResponse> GetAsync(Guid paymentId, string userId, CancellationToken cancellationToken)
    {
        var opId = await ResolvePaymentOperatorAsync(paymentId, userId, cancellationToken);
        var payment = await db.Payments.AsNoTracking()
            .SingleOrDefaultAsync(p => p.OperatorId == opId && p.Id == paymentId &&
                db.Orders.Any(o => o.OperatorId == p.OperatorId && o.Id == p.OrderId && o.PayerUserId == userId), cancellationToken)
            ?? throw new PaymentException("PAYMENT_NOT_FOUND", StatusCodes.Status404NotFound, "Payment not found.");
        return ToResponse(payment, payment.CheckoutUrl);
    }

    public async Task<RetryPaymentResult> RetryAsync(Guid paymentId, string userId, CancellationToken cancellationToken)
    {
        var opId = await ResolvePaymentOperatorAsync(paymentId, userId, cancellationToken);
        var payment = await db.Payments
            .SingleOrDefaultAsync(p => p.OperatorId == opId && p.Id == paymentId &&
                db.Orders.Any(o => o.OperatorId == p.OperatorId && o.Id == p.OrderId && o.PayerUserId == userId), cancellationToken)
            ?? throw new PaymentException("PAYMENT_NOT_FOUND", StatusCodes.Status404NotFound, "Payment not found.");
        if (payment.Status == PaymentStatus.Succeeded)
            return new RetryPaymentResult(ToResponse(payment, null), false);
        var snapshot = await db.Orders.Where(o => o.Id == payment.OrderId).Select(o => o.FareSnapshot).SingleAsync(cancellationToken);
        if (BookingService.IsBooking(snapshot))
        {
            var retryKey = $"pay-retry-{payment.Id:N}";
            var successor = await db.Payments.AsNoTracking().SingleOrDefaultAsync(p => p.Provider == Provider && p.IdempotencyKey == retryKey, cancellationToken);
            if (successor is not null) return new RetryPaymentResult(ToResponse(successor, successor.CheckoutUrl), true);
            if (!BookingPaymentGuard.IsFinalNoCharge(payment))
                throw new PaymentException("BOOKING_RETRY_REQUIRES_RECONCILIATION", 409, "The prior attempt must be verified as finally closed without a charge.");
            var version = await db.Orders.Where(o => o.Id == payment.OrderId).Select(o => o.OrderVersion).SingleAsync(cancellationToken);
            // Repeating retry for one closed attempt always addresses its one successor.
            var next = await CreateAsync(new(payment.OrderId, retryKey, version), userId, cancellationToken);
            return new RetryPaymentResult(next, true);
        }
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

    private async Task<Guid> ResolveOrderOperatorAsync(Guid orderId, string userId, CancellationToken cancellationToken)
    {
        // Resolve the scope from an owned resource, not from staff membership or a client operator id.
        var id = await db.Orders.IgnoreQueryFilters().AsNoTracking()
            .Where(o => o.Id == orderId && o.PayerUserId == userId && o.Operator.Status == "ACTIVE")
            .Select(o => (Guid?)o.OperatorId).SingleOrDefaultAsync(cancellationToken)
            ?? throw new PaymentException("ORDER_NOT_FOUND", StatusCodes.Status404NotFound, "Order not found.");
        operatorContext.CurrentOperatorId = id;
        return id;
    }

    private async Task<Guid> ResolvePaymentOperatorAsync(Guid paymentId, string userId, CancellationToken cancellationToken)
    {
        var id = await db.Payments.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.Id == paymentId && db.Orders.IgnoreQueryFilters().Any(o =>
                o.OperatorId == p.OperatorId && o.Id == p.OrderId && o.PayerUserId == userId && o.Operator.Status == "ACTIVE"))
            .Select(p => (Guid?)p.OperatorId).SingleOrDefaultAsync(cancellationToken)
            ?? throw new PaymentException("PAYMENT_NOT_FOUND", StatusCodes.Status404NotFound, "Payment not found.");
        operatorContext.CurrentOperatorId = id;
        return id;
    }

    private static PaymentResponse ToResponse(PaymentEntity payment, string? checkout) => new(
        payment.Id, payment.OrderId, payment.PaymentCode, payment.Provider, payment.Status.ToString().ToUpperInvariant(),
        payment.AmountMinor, payment.Currency, payment.ProviderPaymentReference, payment.IdempotencyKey,
        payment.InitiatedAt, checkout, payment.AttemptNumber,
        payment.AttemptNumber is null ? null : payment.InitiatedAt.AddMinutes(5));
}
