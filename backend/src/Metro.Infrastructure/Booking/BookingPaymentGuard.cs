using Metro.Application.Abstractions.Booking;
using Metro.Application.Ticketing;
using Metro.Domain.Entities.Orders;
using Metro.Domain.Entities.Finance;
using Metro.Domain.Entities.Ticketing;
using Metro.Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace Metro.Infrastructure.Booking;

public sealed class BookingPaymentGuard(ApplicationDbContext db)
{
    // BE-04 takes this same lock when editing a passenger profile or approving/revoking evidence.
    internal Task LockBeneficiaryEligibilityAsync(string beneficiaryId, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({beneficiaryId}, 4))", ct);

    internal async Task CheckExistingPaymentAndTicketsAsync(Order order, Guid productId, DateTimeOffset now, CancellationToken ct)
    {
        var beneficiary = order.BeneficiaryUserId;
        var op = order.OperatorId;
        // The caller holds the same beneficiary lock as paid/free issuance.
        var busy = await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (
              SELECT 1 FROM metro.orders o
              JOIN metro.payments p ON p.operator_id=o.operator_id AND p.order_id=o.id
              WHERE o.operator_id={op} AND o.beneficiary_user_id={beneficiary}
                AND (p.status IN ('PENDING','REVIEW_REQUIRED') OR
                     p.status IN ('FAILED','EXPIRED','CANCELLED') AND
                       (p.reconciliation_status<>'MATCHED' OR p.verified_at IS NULL))
            ) AS "Value"
            """).SingleAsync(ct);
        if (busy) throw new BookingException("BENEFICIARY_PAYMENT_IN_PROGRESS", 409);

        var productType = await db.TicketProducts.Where(p => p.Id == productId).Select(p => p.ProductType).SingleAsync(ct);
        var (start, end) = TicketRules.Validity(productType, order.ServiceDate, now);
        var conflict = await db.Tickets.AnyAsync(t => t.HolderUserId == beneficiary &&
            ((t.Status == "ISSUED" || t.Status == "ACTIVE" || t.Status == "IN_USE") &&
                  t.EffectiveFrom < end && t.ExpiresAt > start ||
             db.Journeys.Any(j => j.TicketId == t.Id &&
                (j.Status == JourneyStatus.Open || j.Status == JourneyStatus.ManualReview))), ct);
        if (conflict) throw new BookingException("BENEFICIARY_RIGHT_CONFLICT", 409);
        var pendingRight = await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (
              SELECT 1 FROM metro.orders o
              CROSS JOIN LATERAL metro.issuance_bounds(o.operator_id,o.id) bounds
              WHERE o.operator_id={op} AND o.beneficiary_user_id={beneficiary} AND o.id<>{order.Id}
                AND o.status IN ('PAID','PENDING_ISSUE') AND (o.paid_at IS NOT NULL OR o.free_confirmed_at IS NOT NULL)
                AND bounds.start_at<{end} AND bounds.end_at>{start}
            ) AS "Value"
            """).SingleAsync(ct);
        if (pendingRight) throw new BookingException("BENEFICIARY_RIGHT_CONFLICT", 409);
    }

    internal async Task RequireNoUnresolvedAttemptAsync(Order order, CancellationToken ct)
    {
        if (order.PaidAt is not null || order.FreeConfirmedAt is not null)
            throw new BookingException("BOOKING_ALREADY_STARTED_OR_CLOSED", 409);
        var attempts = await db.Payments.Where(p => p.OrderId == order.Id).ToListAsync(ct);
        if (attempts.Any(p => !IsFinalNoCharge(p)))
            throw new BookingException("BOOKING_PAYMENT_UNRESOLVED", 409);
    }

    internal static bool IsFinalNoCharge(Payment p) =>
        p.Status is PaymentStatus.Failed or PaymentStatus.Cancelled or PaymentStatus.Expired &&
        p.VerifiedAt is not null && p.SucceededAt is null && p.ReconciliationStatus == PaymentReconciliationStatus.Matched;
}
