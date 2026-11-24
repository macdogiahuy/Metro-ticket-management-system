using System.Security.Cryptography;
using System.Text.Json;
using Metro.Application.Abstractions.Booking;
using Metro.Application.Abstractions.MultiTenancy;
using Metro.Application.Booking;
using Metro.Application.Ticketing;
using Metro.Domain.Entities.Orders;
using Metro.Domain.Entities.Ticketing;
using Metro.Domain.Entities.Passengers;
using Metro.Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Metro.Infrastructure.Booking;

public sealed class BookingService(ApplicationDbContext db, IOperatorContext scope, BookingPricing pricing,
    BookingPaymentGuard paymentGuard, TimeProvider time)
{
    public async Task<BookingResponse> CreateAsync(string actor, string operatorCode, BookingSelection selection, string key, CancellationToken ct)
    {
        var op = await db.Operators.AsNoTracking().SingleOrDefaultAsync(o => o.Code == operatorCode && o.Status == "ACTIVE", ct)
            ?? throw new BookingException("BOOKING_OPERATOR_NOT_FOUND", 404);
        scope.CurrentOperatorId = op.Id;
        return await WriteAsync(actor, "create", key, selection, async () =>
        {
            var now = time.GetUtcNow();
            var order = new Order
            {
                OperatorId = op.Id, OrderCode = $"ORD-{Guid.NewGuid():N}", PayerUserId = actor, CreatedByUserId = actor,
                CreatedAt = now.UtcDateTime, UpdatedAt = now.UtcDateTime
            };
            var item = new OrderItem { OperatorId = op.Id, OrderId = order.Id, CreatedAt = now.UtcDateTime };
            db.AddRange(order, item);
            return await SetVersionAsync(order, item, selection, op.Code, actor, now, ct);
        }, ct);
    }

    public async Task<BookingResponse> ReviseAsync(string actor, Guid id, ReviseBookingRequest request, string key, CancellationToken ct)
    {
        if (request.Selection is null) throw new BookingException("BOOKING_SELECTION_REQUIRED", 400);
        var opCode = await ResolveAsync(actor, id, ct);
        return await WriteAsync(actor, "revise", key, new { id, request }, async () =>
        {
            var order = await LockAsync(id, ct);
            RequireBuyer(order, actor);
            var now = time.GetUtcNow();
            await RequireEditableAsync(order, request.OrderVersion, now, ct);
            var (version, item, consent) = await DetailsAsync(order, ct);
            version.Status = "SUPERSEDED";
            if (consent?.Status is PurchaseConsentStatus.Requested or PurchaseConsentStatus.Accepted)
            {
                consent.Status = PurchaseConsentStatus.Withdrawn;
                consent.RespondedAt = now;
                await QueueConsentAsync(order, consent, ct);
            }
            await db.SaveChangesAsync(ct);
            order.OrderVersion++;
            order.ConsentId = null;
            return await SetVersionAsync(order, item, request.Selection, opCode, actor, now, ct);
        }, ct);
    }

    public async Task<BookingResponse> ConfirmAsync(string actor, Guid id, ConfirmBookingRequest request, string key, CancellationToken ct)
    {
        var opCode = await ResolveAsync(actor, id, ct);
        return await WriteAsync(actor, "confirm", key, new { id, request }, async () =>
        {
            var order = await LockAsync(id, ct);
            RequireBuyer(order, actor);
            var now = time.GetUtcNow();
            await RequireEditableAsync(order, request.OrderVersion, now, ct);
            var (version, item, consent) = await DetailsAsync(order, ct);
            if (order.TotalMinor != request.ExpectedTotalMinor)
                throw new BookingException("CHECKOUT_PRICE_CHANGED", 409);
            if (version.EligibilityGroup == "CHILD_UNDER_6")
            {
                await paymentGuard.LockBeneficiaryEligibilityAsync(order.BeneficiaryUserId!, ct);
                now = time.GetUtcNow();
                await RequireEditableAsync(order, request.OrderVersion, now, ct);
            }
            await CheckCheckoutAsync(order, version, item, consent, opCode, now, ct);
            await paymentGuard.CheckExistingPaymentAndTicketsAsync(order, item.TicketProductId, now, ct);
            if (version.Status == "DRAFT")
            {
                version.Status = "CONFIRMED";
                version.ConfirmedAt = now.UtcDateTime;
                order.Status = "PENDING_PAYMENT";
                order.UpdatedAt = now.UtcDateTime;
            }
            if (version.EligibilityGroup == "CHILD_UNDER_6")
            {
                await db.SaveChangesAsync(ct);
                var guardianTicket = await db.Database.SqlQuery<bool>($"""
                    SELECT EXISTS(SELECT 1 FROM metro.eligible_guardian_ticket({order.OperatorId},{id})
                        WHERE guardian_user_id={actor}) AS "Value"
                    """).SingleAsync(ct);
                if (!guardianTicket) throw new BookingException("BOOKING_GUARDIAN_TICKET_REQUIRED", 409);
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT metro.issue_free_order({order.OperatorId},{id},{actor},'CHECKOUT_CONFIRMED')", ct);
                await db.Entry(order).ReloadAsync(ct);
            }
            return ToResponse(order, version, consent, actor, now);
        }, ct);
    }

    public async Task<BookingResponse> RespondAsync(string actor, Guid id, BookingConsentRequest request, string key, CancellationToken ct)
    {
        await ResolveAsync(actor, id, ct);
        return await WriteAsync(actor, "consent", key, new { id, request }, async () =>
        {
            var order = await LockAsync(id, ct);
            var now = time.GetUtcNow();
            await RequireEditableAsync(order, request.OrderVersion, now, ct);
            var (version, _, consent) = await DetailsAsync(order, ct);
            var recipient = actor == order.BeneficiaryUserId;
            var buyer = actor == order.PayerUserId;
            if (!recipient && !buyer) throw new BookingException("BOOKING_FORBIDDEN", 403);
            var decision = request.Decision switch
            {
                "ACCEPT" when recipient => PurchaseConsentStatus.Accepted,
                "DECLINE" when recipient => PurchaseConsentStatus.Declined,
                "WITHDRAW" when recipient || buyer => PurchaseConsentStatus.Withdrawn,
                "ACCEPT" or "DECLINE" or "WITHDRAW" => throw new BookingException("CONSENT_FORBIDDEN", 403),
                _ => throw new BookingException("INVALID_CONSENT_DECISION", 400)
            };
            if (consent is null || consent.Id != request.ConsentId || consent.ExpiresAt <= now ||
                consent.OrderContentHash != ContentHash(version) ||
                !(consent.Status == PurchaseConsentStatus.Requested ||
                    consent.Status == PurchaseConsentStatus.Accepted && decision == PurchaseConsentStatus.Withdrawn))
                throw new BookingException("CONSENT_NOT_CURRENT", 409);
            await RequireActiveAsync(actor, ct);
            consent.Status = decision;
            consent.RespondedAt = now;
            order.UpdatedAt = now.UtcDateTime;
            await QueueConsentAsync(order, consent, ct);
            return ToResponse(order, version, consent, actor, now);
        }, ct);
    }

    public async Task<BookingResponse> CancelAsync(string actor, Guid id, int versionNumber, string key, CancellationToken ct)
    {
        await ResolveAsync(actor, id, ct);
        return await WriteAsync(actor, "cancel", key, new { id, versionNumber }, async () =>
        {
            var order = await LockAsync(id, ct);
            RequireBuyer(order, actor);
            await RequireActiveAsync(actor, ct);
            var now = time.GetUtcNow();
            await RequireEditableAsync(order, versionNumber, now, ct);
            var (version, _, consent) = await DetailsAsync(order, ct);
            version.Status = "CANCELLED";
            if (consent?.Status is PurchaseConsentStatus.Requested or PurchaseConsentStatus.Accepted)
            {
                consent.Status = PurchaseConsentStatus.Withdrawn;
                consent.RespondedAt = now;
                await QueueConsentAsync(order, consent, ct);
            }
            var op = order.OperatorId;
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT metro.cancel_unpaid_order({op},{id},{actor},'BUYER_CANCELLED')", ct);
            await db.Entry(order).ReloadAsync(ct);
            return ToResponse(order, version, consent, actor, now);
        }, ct);
    }

    public async Task<BookingResponse> GetAsync(string actor, Guid id, CancellationToken ct)
    {
        await ResolveAsync(actor, id, ct);
        var order = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == id, ct);
        EnsureParty(order, actor);
        var consent = await db.PurchaseConsents.AsNoTracking().SingleOrDefaultAsync(c => c.Id == order.ConsentId, ct);
        var version = await db.OrderVersions.AsNoTracking()
            .SingleAsync(v => v.OrderId == id && v.Version == order.OrderVersion, ct);
        return ToResponse(order, version, consent, actor, time.GetUtcNow());
    }

    public async Task<TripSuggestionResponse> GetSuggestionAsync(string actor, Guid id, CancellationToken ct)
    {
        var booking = await GetAsync(actor, id, ct);
        var suggestion = await db.TripSuggestions.AsNoTracking().SingleOrDefaultAsync(s =>
            s.OrderId == id && s.OrderVersion == booking.OrderVersion && s.SupersededAt == null, ct);
        if (suggestion is null)
        {
            var status = booking.ServiceDate is null ? "NOT_APPLICABLE" :
                booking.Status is "PAID" or "PENDING_ISSUE" or "ISSUED" ? "PENDING" : "NOT_AVAILABLE";
            return new(booking.OrderVersion, status, null, null, null, null, null, null, null);
        }
        return new(booking.OrderVersion, suggestion.Status == TripSuggestionStatus.Suggested ? "SUGGESTED" : "NO_TRIP",
            suggestion.SuggestedPlannedTripId, suggestion.LineId, suggestion.Direction, suggestion.SuggestedDepartureAt,
            suggestion.NoTripReason, suggestion.Source.ToString().ToUpperInvariant(), suggestion.ComputedAt);
    }

    public async Task<IReadOnlyList<Guid>> PendingConsentsAsync(string actor, int page, CancellationToken ct)
    {
        if (page is < 1 or > 1000) throw new BookingException("INVALID_PAGE", 400);
        await RequireActiveAsync(actor, ct);
        var now = time.GetUtcNow();
        return await db.PurchaseConsents.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.BeneficiaryUserId == actor && c.Status == PurchaseConsentStatus.Requested && c.ExpiresAt > now &&
                db.Orders.IgnoreQueryFilters().Any(o => o.Id == c.OrderId && o.OperatorId == c.OperatorId &&
                    o.ConsentId == c.Id && (o.Status == "DRAFT" || o.Status == "PENDING_PAYMENT") && o.Operator.Status == "ACTIVE"))
            .OrderByDescending(c => c.RequestedAt).ThenBy(c => c.Id).Skip((page - 1) * 20).Take(20).Select(c => c.OrderId).ToListAsync(ct);
    }

    private async Task<BookingResponse> SetVersionAsync(Order order, OrderItem item, BookingSelection selection,
        string operatorCode, string actor, DateTimeOffset now, CancellationToken ct)
    {
        var beneficiary = selection.BeneficiaryUserId ?? actor;
        if (selection.BeneficiaryEmail is not null)
        {
            var email = selection.BeneficiaryEmail.Trim().ToUpperInvariant();
            if (email.Length is < 3 or > 254) throw new BookingException("INVALID_BENEFICIARY_EMAIL", 400);
            var account = await db.Users.AsNoTracking().Where(u => u.NormalizedEmail == email)
                .Select(u => u.Id).SingleOrDefaultAsync(ct)
                ?? throw new BookingException("BOOKING_ACCOUNT_INELIGIBLE", 403);
            if (selection.BeneficiaryUserId is not null && account != selection.BeneficiaryUserId)
                throw new BookingException("BENEFICIARY_SELECTION_CONFLICT", 400);
            beneficiary = account;
        }
        var actorBirth = await RequireActiveAsync(actor, ct);
        var birth = beneficiary == actor ? actorBirth : await RequireActiveAsync(beneficiary, ct);
        var (product, price) = await pricing.QuoteAsync(selection, beneficiary, birth, operatorCode, now, TicketRules.LocalDate(order.CreatedAt), ct);
        if (price.AppliedGroup == "CHILD_UNDER_6") await RequireGuardianAsync(actor, beneficiary, selection.ServiceDate!.Value, now, ct);
        if (BookingRules.Deadline(order.CreatedAt, selection.ServiceDate) <= now)
            throw new BookingException("BOOKING_EXPIRED", 409);
        var snapshot = JsonSerializer.Serialize(price);
        order.BeneficiaryUserId = beneficiary;
        order.PayerNameSnapshot ??= await db.UserProfiles.Where(p => p.UserId == actor).Select(p => p.FullName).SingleAsync(ct);
        order.PayerEmailSnapshot ??= await db.Users.Where(u => u.Id == actor).Select(u => u.Email).SingleAsync(ct);
        order.BeneficiaryNameSnapshot = await db.UserProfiles.Where(p => p.UserId == beneficiary).Select(p => p.FullName).SingleAsync(ct);
        order.FarePolicyVersionId = price.PolicyId;
        order.SubtotalMinor = price.Subtotal;
        order.DiscountMinor = price.Discount;
        order.TotalMinor = price.Amount;
        order.EligibilityGroup = price.AppliedGroup;
        order.AgeReferenceDate = price.AgeReferenceDate;
        order.ServiceDate = selection.ServiceDate;
        order.FareSnapshot = snapshot;
        order.UpdatedAt = now.UtcDateTime;

        item.TicketProductId = product.Id;
        item.OriginStationId = selection.OriginStationId;
        item.DestinationStationId = selection.DestinationStationId;
        item.UnitPriceMinor = price.Subtotal;
        item.DiscountMinor = price.Discount;
        item.LineTotalMinor = price.Amount;
        item.FareSnapshot = snapshot;
        item.ProductSnapshot = JsonSerializer.Serialize(new { product.Code, product.ProductType });

        var version = new OrderVersion
        {
            OperatorId = order.OperatorId, OrderId = order.Id, Version = order.OrderVersion, BeneficiaryUserId = beneficiary,
            TicketProductId = product.Id, OriginStationId = item.OriginStationId, DestinationStationId = item.DestinationStationId,
            ServiceDate = order.ServiceDate, EligibilityGroup = order.EligibilityGroup, AgeReferenceDate = price.AgeReferenceDate,
            SubtotalMinor = price.Subtotal, DiscountMinor = price.Discount,
            TotalMinor = price.Amount, FarePolicyVersionId = price.PolicyId, FareSnapshot = snapshot,
            CreatedByUserId = actor, CreatedAt = now.UtcDateTime,
            ChangeReason = order.OrderVersion > 1 ? "CHECKOUT_UPDATED" : null
        };
        db.OrderVersions.Add(version);
        await db.SaveChangesAsync(ct);

        PurchaseConsent? consent = null;
        if (actor != beneficiary && price.AppliedGroup != "CHILD_UNDER_6")
        {
            consent = new PurchaseConsent
            {
                Id = Guid.NewGuid(), CorrelationId = Guid.NewGuid(), OrderId = order.Id, OperatorId = order.OperatorId,
                OrderVersion = version.Version, BuyerUserId = actor, BeneficiaryUserId = beneficiary,
                OrderContentHash = ContentHash(version), RequestedAt = now, ExpiresAt = BookingRules.Deadline(now, version.ServiceDate)
            };
            db.PurchaseConsents.Add(consent);
            await db.SaveChangesAsync(ct);
            order.ConsentId = consent.Id;
            await QueueConsentAsync(order, consent, ct);
        }
        return ToResponse(order, version, consent, actor, now);
    }

    private Task<BookingResponse> WriteAsync(string actor, string operation, string key, object payload,
        Func<Task<BookingResponse>> action, CancellationToken ct) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(async _ =>
    {
        db.ChangeTracker.Clear();
        BookingRules.ValidateKey(key);
        var op = scope.CurrentOperatorId!.Value;
        var keyHash = Hash(key);
        var payloadHash = Hash(payload);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var lockKey = $"booking:{op}:{actor}:{operation}:{keyHash}";
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({lockKey},0))", ct);
        var existing = await db.Database.SqlQuery<BookingReplay>($"""
            SELECT payload_hash AS "PayloadHash", response::text AS "Response" FROM metro.booking_requests
            WHERE operator_id={op} AND actor_user_id={actor} AND operation={operation} AND request_key_hash={keyHash}
            """).SingleOrDefaultAsync(ct);
        if (existing is not null)
        {
            if (existing.PayloadHash != payloadHash) throw new BookingException("IDEMPOTENCY_KEY_CONFLICT", 409);
            return JsonSerializer.Deserialize<BookingResponse>(existing.Response)!;
        }
        var result = await action();
        await db.SaveChangesAsync(ct);
        var json = JsonSerializer.Serialize(result);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO metro.audit_logs (operator_id,actor_user_id,action,entity_type,entity_id,changed_fields,outcome)
            VALUES ({op},{actor},{"BOOKING_" + operation.ToUpperInvariant()},'ORDER',{result.Id.ToString()},ARRAY['order_version','status'],'SUCCEEDED')
            """, ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO metro.booking_requests (operator_id, actor_user_id, operation, request_key_hash, payload_hash, response)
            VALUES ({op}, {actor}, {operation}, {keyHash}, {payloadHash}, CAST({json} AS jsonb))
            """, ct);
        await tx.CommitAsync(ct);
        return result;
    }, ct);

    private async Task<string> ResolveAsync(string actor, Guid id, CancellationToken ct)
    {
        var owner = await db.Orders.IgnoreQueryFilters().AsNoTracking()
            .Where(o => o.Id == id && o.Operator.Status == "ACTIVE")
            .Select(o => new { o.OperatorId, o.Operator.Code, o.PayerUserId, o.BeneficiaryUserId, o.FareSnapshot })
            .SingleOrDefaultAsync(ct);
        if (owner is null) throw new BookingException("BOOKING_NOT_FOUND", 404);
        if (owner.PayerUserId != actor && owner.BeneficiaryUserId != actor)
            throw new BookingException("BOOKING_FORBIDDEN", 403);
        if (!IsBooking(owner.FareSnapshot)) throw new BookingException("BOOKING_NOT_FOUND", 404);
        scope.CurrentOperatorId = owner.OperatorId;
        return owner.Code;
    }

    internal async Task<Order> LockAsync(Guid id, CancellationToken ct)
    {
        var op = scope.CurrentOperatorId!.Value;
        var beneficiary = await db.Orders.AsNoTracking().Where(o => o.Id == id).Select(o => o.BeneficiaryUserId).SingleAsync(ct);
        var beneficiaryLock = $"{op}:{beneficiary}";
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({beneficiaryLock},0))", ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM metro.orders WHERE operator_id={op} AND id={id} FOR UPDATE", ct);
        var order = await db.Orders.SingleAsync(o => o.Id == id, ct);
        if (order.BeneficiaryUserId != beneficiary) throw new BookingException("ORDER_VERSION_CONFLICT", 409);
        return order;
    }

    private async Task<(OrderVersion Version, OrderItem Item, PurchaseConsent? Consent)> DetailsAsync(Order order, CancellationToken ct)
    {
        var version = await db.OrderVersions.SingleAsync(v => v.OrderId == order.Id && v.Version == order.OrderVersion, ct);
        var item = await db.OrderItems.SingleAsync(i => i.OrderId == order.Id, ct);
        var consent = await db.PurchaseConsents.SingleOrDefaultAsync(c => c.Id == order.ConsentId, ct);
        return (version, item, consent);
    }

    private async Task<DateOnly> RequireActiveAsync(string userId, CancellationToken ct)
    {
        var birthDate = await (from profile in db.UserProfiles
            join user in db.Users on profile.UserId equals user.Id
            where profile.UserId == userId && profile.Status == "ACTIVE" && user.EmailConfirmed &&
                (user.LockoutEnd == null || user.LockoutEnd <= time.GetUtcNow())
            select (DateOnly?)profile.BirthDate).SingleOrDefaultAsync(ct);
        return birthDate ?? throw new BookingException("BOOKING_ACCOUNT_INELIGIBLE", 403);
    }

    private static void RequireBuyer(Order order, string actor)
    {
        if (order.PayerUserId != actor) throw new BookingException("BOOKING_BUYER_REQUIRED", 403);
    }

    private static void EnsureParty(Order order, string actor)
    {
        if (order.PayerUserId != actor && order.BeneficiaryUserId != actor)
            throw new BookingException("BOOKING_FORBIDDEN", 403);
    }

    private async Task RequireEditableAsync(Order order, int version, DateTimeOffset now, CancellationToken ct)
    {
        if (order.OrderVersion != version) throw new BookingException("ORDER_VERSION_CONFLICT", 409);
        if (order.Status is not ("DRAFT" or "PENDING_PAYMENT"))
            throw new BookingException("BOOKING_ALREADY_STARTED_OR_CLOSED", 409);
        if (BookingRules.Deadline(order.CreatedAt, order.ServiceDate) <= now)
            throw new BookingException("BOOKING_EXPIRED", 409);
        await paymentGuard.RequireNoUnresolvedAttemptAsync(order, ct);
    }

    private async Task CheckCheckoutAsync(Order order, OrderVersion version, OrderItem item,
        PurchaseConsent? consent, string opCode, DateTimeOffset now, CancellationToken ct)
    {
        await RequireActiveAsync(order.PayerUserId!, ct);
        var birth = await RequireActiveAsync(order.BeneficiaryUserId!, ct);
        var freeChild = version.EligibilityGroup == "CHILD_UNDER_6";
        if (freeChild) await RequireGuardianAsync(order.PayerUserId!, order.BeneficiaryUserId!, order.ServiceDate!.Value, now, ct);
        if (!freeChild && order.PayerUserId != order.BeneficiaryUserId &&
            (consent is not { Status: PurchaseConsentStatus.Accepted } || consent.ExpiresAt <= now ||
             consent.OrderVersion != order.OrderVersion || consent.BuyerUserId != order.PayerUserId ||
             consent.BeneficiaryUserId != order.BeneficiaryUserId || consent.OrderContentHash != ContentHash(version)))
            throw new BookingException("BOOKING_CONSENT_REQUIRED", 409);

        var old = JsonSerializer.Deserialize<CheckoutPrice>(version.FareSnapshot)!;
        var selection = new BookingSelection(item.TicketProductId, order.BeneficiaryUserId,
            order.ServiceDate, item.OriginStationId, item.DestinationStationId, old.JourneyOptionIndex, old.RequestedGroup);
        var (_, current) = await pricing.QuoteAsync(selection, order.BeneficiaryUserId!, birth, opCode, now, TicketRules.LocalDate(order.CreatedAt), ct);
        if (old != current || order.TotalMinor != old.Amount || order.FarePolicyVersionId != old.PolicyId)
            throw new BookingException("CHECKOUT_PRICE_OR_ROUTE_CHANGED", 409);
    }

    internal static bool IsBooking(string snapshot)
    {
        using var json = JsonDocument.Parse(snapshot);
        return json.RootElement.ValueKind == JsonValueKind.Object &&
            json.RootElement.TryGetProperty(nameof(CheckoutPrice.BookingCheckout), out var value) &&
            value.ValueKind == JsonValueKind.True;
    }

    internal async Task<DateTimeOffset> ValidatePaymentStartAsync(Order order, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        await RequireEditableAsync(order, order.OrderVersion, now, ct);
        var (version, item, consent) = await DetailsAsync(order, ct);
        if (order.Status != "PENDING_PAYMENT" || version.Status != "CONFIRMED")
            throw new BookingException("CHECKOUT_CONFIRMATION_REQUIRED", 409);
        await paymentGuard.LockBeneficiaryEligibilityAsync(order.BeneficiaryUserId!, ct);
        var opCode = await db.Operators.Where(o => o.Id == order.OperatorId).Select(o => o.Code).SingleAsync(ct);
        now = time.GetUtcNow();
        await RequireEditableAsync(order, order.OrderVersion, now, ct);
        await CheckCheckoutAsync(order, version, item, consent, opCode, now, ct);
        await paymentGuard.CheckExistingPaymentAndTicketsAsync(order, item.TicketProductId, now, ct);
        return now;
    }

    private static BookingResponse ToResponse(Order o, OrderVersion v, PurchaseConsent? c, string actor, DateTimeOffset now)
    {
        var deadline = BookingRules.Deadline(o.CreatedAt, o.ServiceDate);
        if (c is not null && c.ExpiresAt < deadline) deadline = c.ExpiresAt;
        var consentStatus = c?.Status is PurchaseConsentStatus.Requested or PurchaseConsentStatus.Accepted &&
            c.ExpiresAt <= now && o.PaymentStartedAt is null ? "EXPIRED" : c?.Status.ToString().ToUpperInvariant();
        var status = v.Status == "DRAFT" || c is not null && consentStatus != "ACCEPTED" && o.Status == "PENDING_PAYMENT"
            ? "DRAFT" : o.Status;
        var price = JsonSerializer.Deserialize<CheckoutPrice>(v.FareSnapshot)!;
        return new(o.Id, o.OrderCode, v.Version, status, v.TicketProductId, v.ServiceDate, v.OriginStationId, v.DestinationStationId,
            o.PayerUserId != o.BeneficiaryUserId, o.PayerUserId == actor, v.TotalMinor, o.Currency,
            v.FarePolicyVersionId, deadline, c?.Id, consentStatus, c?.ExpiresAt, v.DiscountMinor,
            price.RequestedGroup, price.AppliedGroup, v.AgeReferenceDate, price.AppliedGroup == "CHILD_UNDER_6",
            o.PayerNameSnapshot, o.BeneficiaryNameSnapshot);
    }

    private async Task RequireGuardianAsync(string actor, string child, DateOnly day, DateTimeOffset now, CancellationToken ct)
    {
        var today = TicketRules.LocalDate(now);
        var op = scope.CurrentOperatorId!.Value;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT 1 FROM metro.guardian_links WHERE operator_id={op} AND guardian_user_id={actor}
                AND child_user_id={child} FOR SHARE
            """, ct);
        if (!await db.GuardianLinks.AnyAsync(g => g.GuardianUserId == actor && g.ChildUserId == child &&
            g.Status == GuardianLinkStatus.Active && g.VerificationType == GuardianVerificationType.Guardianship &&
            g.VerifiedAt != null && g.VerifiedByUserId != null &&
            (g.ValidFrom == null || g.ValidFrom <= today && g.ValidFrom <= day) &&
            (g.ValidTo == null || g.ValidTo >= today && g.ValidTo >= day), ct))
            throw new BookingException("BOOKING_GUARDIAN_REQUIRED", 403);
    }

    private async Task QueueConsentAsync(Order order, PurchaseConsent consent, CancellationToken ct)
    {
        // SRS 8.2: the buyer and recipient receive website + email notifications.
        // Enqueue only identifiers; delivery is independent and must check action expiry.
        var state = consent.Status.ToString().ToUpperInvariant();
        var notice = JsonSerializer.Serialize(new { eventId = $"{consent.Id:N}:{state}",
            orderId = order.Id, orderVersion = consent.OrderVersion, consentId = consent.Id,
            consentStatus = state, expiresAt = consent.ExpiresAt });
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO metro.notifications (operator_id,recipient_user_id,notification_type,channel,content_reference,mandatory)
            SELECT {order.OperatorId},recipient.user_id,{"PURCHASE_CONSENT_" + state},channel.name,CAST({notice} AS jsonb),true
            FROM (SELECT DISTINCT user_id FROM (VALUES ({consent.BuyerUserId}),({consent.BeneficiaryUserId})) parties(user_id)) recipient
            CROSS JOIN (VALUES ('IN_APP'),('EMAIL_MOCK')) channel(name)
            """, ct);
    }

    private static string ContentHash(OrderVersion v) => Hash(new
    {
        v.OrderId, v.Version, v.BeneficiaryUserId, v.TicketProductId, v.ServiceDate, v.OriginStationId, v.DestinationStationId
    });

    private static string Hash(object value) => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));

    private sealed record BookingReplay(string PayloadHash, string Response);
}
