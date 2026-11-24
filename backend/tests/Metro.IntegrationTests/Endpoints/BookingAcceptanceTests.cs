using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Metro.Application.Abstractions.Booking;
using Metro.Application.Abstractions.Payment;
using Metro.Infrastructure.Booking;
using Metro.Infrastructure.Persistence.Context;
using Metro.IntegrationTests.Infrastructure;
using Metro.IntegrationTests.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using static Metro.IntegrationTests.Payments.PaymentScenarioFixture;

namespace Metro.IntegrationTests.Endpoints;

public sealed partial class BookingEndpointsTests
{
    [PostgreSqlFact, Trait("Category", "PostgreSQL")]
    public async Task Every_booking_route_requires_authentication_and_unrelated_users_cannot_write()
    {
        var seed = await SeedAsync();
        using var buyer = await ClientAsync(seed.Buyer);
        using var other = await ClientAsync(seed.Other);
        using var anonymous = factory.CreateClient();
        var input = new BookingSelection(seed.DayPass);
        var create = $"/api/v1/orders?operatorCode={seed.Code}";
        var order = await Booking(await Send(buyer, HttpMethod.Post, create, input), HttpStatusCode.Created);
        var path = $"/api/v1/orders/{order.Id}";
        foreach (var (method, url, body) in new (HttpMethod, string, object?)[]
        {
            (HttpMethod.Post, create, input), (HttpMethod.Get, path, null),
            (HttpMethod.Put, path, new ReviseBookingRequest(1, input)),
            (HttpMethod.Post, path + "/confirm", new ConfirmBookingRequest(1, order.TotalMinor)),
            (HttpMethod.Post, path + "/consent", new BookingConsentRequest(1, Guid.NewGuid(), "ACCEPT")),
            (HttpMethod.Delete, path + "?orderVersion=1", null),
            (HttpMethod.Get, path + "/suggestion", null), (HttpMethod.Get, "/api/v1/orders/consents", null)
        })
        {
            await Expect(await Send(anonymous, method, url, body), HttpStatusCode.Unauthorized);
            if (method != HttpMethod.Get && url != create) await Expect(await Send(other, method, url, body), HttpStatusCode.Forbidden);
        }
    }

    [PostgreSqlFact, Trait("Category", "PostgreSQL")]
    public async Task Each_booking_write_replays_without_extra_versions_consents_or_audit_events()
    {
        var seed = await SeedAsync();
        using var buyer = await ClientAsync(seed.Buyer);
        using var recipient = await ClientAsync(seed.Recipient);
        var input = new BookingSelection(seed.DayPass, BeneficiaryEmail: $"{seed.Recipient}@example.invalid");
        var order = await Booking(await Send(buyer, HttpMethod.Post, $"/api/v1/orders?operatorCode={seed.Code}", input), HttpStatusCode.Created);
        var path = $"/api/v1/orders/{order.Id}";
        var consent = new BookingConsentRequest(1, order.ConsentId!.Value, "ACCEPT");
        var key = Guid.NewGuid().ToString();
        var accepted = await Booking(await Send(recipient, HttpMethod.Post, path + "/consent", consent, key));
        Assert.Equal(accepted, await Booking(await Send(recipient, HttpMethod.Post, path + "/consent", consent, key)));
        key = Guid.NewGuid().ToString();
        var confirm = new ConfirmBookingRequest(1, order.TotalMinor);
        var confirmed = await Booking(await Send(buyer, HttpMethod.Post, path + "/confirm", confirm, key));
        Assert.Equal(confirmed, await Booking(await Send(buyer, HttpMethod.Post, path + "/confirm", confirm, key)));
        key = Guid.NewGuid().ToString();
        var revision = new ReviseBookingRequest(1, input);
        var revised = await Booking(await Send(buyer, HttpMethod.Put, path, revision, key));
        Assert.Equal(revised, await Booking(await Send(buyer, HttpMethod.Put, path, revision, key)));
        key = Guid.NewGuid().ToString();
        var cancelled = await Booking(await Send(buyer, HttpMethod.Delete, path + "?orderVersion=2", null, key));
        Assert.Equal(cancelled, await Booking(await Send(buyer, HttpMethod.Delete, path + "?orderVersion=2", null, key)));
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(2, await db.OrderVersions.IgnoreQueryFilters().CountAsync(v => v.OrderId == order.Id));
        Assert.Equal(2, await db.PurchaseConsents.IgnoreQueryFilters().CountAsync(c => c.OrderId == order.Id));
        Assert.Equal(5, await db.AuditLogs.IgnoreQueryFilters().CountAsync(a => a.EntityId == order.Id.ToString() && a.Action.StartsWith("BOOKING_")));
        var notices = await db.Database.SqlQuery<int>($"""
            SELECT count(*)::integer AS "Value" FROM metro.notifications
            WHERE operator_id={seed.Op} AND content_reference->>'orderId'={order.Id.ToString()}
            """).SingleAsync();
        Assert.Equal(20, notices); // Five consent events, two recipients, two channels; replays add none.
    }

    [PostgreSqlFact, Trait("Category", "PostgreSQL")]
    public async Task Payment_replays_and_only_verified_no_charge_can_reopen_checkout_with_three_immutable_attempts()
    {
        var seed = await SeedAsync();
        using var buyer = await ClientAsync(seed.Buyer);
        var input = new BookingSelection(seed.DayPass);
        var order = await Booking(await Send(buyer, HttpMethod.Post, $"/api/v1/orders?operatorCode={seed.Code}", input), HttpStatusCode.Created);
        var path = $"/api/v1/orders/{order.Id}";
        await Booking(await Send(buyer, HttpMethod.Post, path + "/confirm", new ConfirmBookingRequest(1, order.TotalMinor)));
        var request = new CreatePaymentRequest(order.Id, Guid.NewGuid().ToString(), 1);
        var firstResponse = await buyer.PostAsJsonAsync("/api/v1/payments", request);
        await Expect(firstResponse, HttpStatusCode.Created);
        var first = (await firstResponse.Content.ReadFromJsonAsync<PaymentResponse>())!;
        var replay = await buyer.PostAsJsonAsync("/api/v1/payments", request);
        await Expect(replay, HttpStatusCode.Created);
        Assert.Equal(first, await replay.Content.ReadFromJsonAsync<PaymentResponse>());
        Assert.Equal(1, first.AttemptNumber);
        Assert.Equal(first.InitiatedAt.AddMinutes(5), first.WaitExpiresAt);
        await Expect(await buyer.PostAsJsonAsync("/api/v1/payments", request with { OrderVersion = 2 }), HttpStatusCode.Conflict);
        await Expect(await Send(buyer, HttpMethod.Put, path, new ReviseBookingRequest(1, input)), HttpStatusCode.Conflict);
        await Expect(await Send(buyer, HttpMethod.Delete, path + "?orderVersion=1", null), HttpStatusCode.Conflict);
        await Expect(await buyer.PostAsync($"/api/v1/payments/{first.Id}/retry", null), HttpStatusCode.Conflict);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE metro.payments SET status='FAILED',reconciliation_status='MATCHED' WHERE id={first.Id}");
        await Expect(await Send(buyer, HttpMethod.Put, path, new ReviseBookingRequest(1, input)), HttpStatusCode.Conflict);
        // Fixture represents the authoritative final no-charge outcome supplied by BE-08.
        await FinalNoChargeAsync(db, first.Id);
        var revised = await Booking(await Send(buyer, HttpMethod.Put, path, new ReviseBookingRequest(1, input)));
        Assert.Equal(2, revised.OrderVersion);
        await Expect(await buyer.PostAsync($"/api/v1/payments/{first.Id}/retry", null), HttpStatusCode.Conflict);
        await Booking(await Send(buyer, HttpMethod.Post, path + "/confirm", new ConfirmBookingRequest(2, revised.TotalMinor)));
        var secondResponse = await buyer.PostAsync($"/api/v1/payments/{first.Id}/retry", null);
        await Expect(secondResponse, HttpStatusCode.OK);
        var second = (await secondResponse.Content.ReadFromJsonAsync<RetryPaymentResult>())!.Payment;
        Assert.NotEqual(first.Id, second.Id);
        Assert.NotEqual(first.PaymentCode, second.PaymentCode);
        Assert.Equal(2, second.AttemptNumber);
        var secondReplay = await buyer.PostAsync($"/api/v1/payments/{first.Id}/retry", null);
        await Expect(secondReplay, HttpStatusCode.OK);
        Assert.Equal(second.Id, (await secondReplay.Content.ReadFromJsonAsync<RetryPaymentResult>())!.Payment.Id);
        await FinalNoChargeAsync(db, second.Id);
        var thirdResponse = await buyer.PostAsync($"/api/v1/payments/{second.Id}/retry", null);
        await Expect(thirdResponse, HttpStatusCode.OK);
        var third = (await thirdResponse.Content.ReadFromJsonAsync<RetryPaymentResult>())!.Payment;
        Assert.Equal(3, third.AttemptNumber);
        await FinalNoChargeAsync(db, third.Id);
        await Expect(await buyer.PostAsync($"/api/v1/payments/{third.Id}/retry", null), HttpStatusCode.Conflict);
        var attempts = await db.Payments.IgnoreQueryFilters().Where(p => p.OrderId == order.Id).OrderBy(p => p.AttemptNumber).ToListAsync();
        Assert.Equal(3, attempts.Count);
        Assert.Equal(1, attempts[0].OrderVersion);
        Assert.Equal(2, attempts[1].OrderVersion);
        Assert.Equal(first.IdempotencyKey, attempts[0].IdempotencyKey);
        Assert.Equal("CANCELLED", (await Booking(await Send(buyer, HttpMethod.Delete, path + "?orderVersion=2", null))).Status);
    }

    [PostgreSqlFact, Trait("Category", "PostgreSQL")]
    public async Task Free_child_checkout_uses_verified_guardian_and_paid_ticket_without_payment_or_early_QR()
    {
        var seed = await SeedAsync();
        using var guardian = await ClientAsync(seed.Buyer);
        using var stranger = await ClientAsync(seed.Other);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var birth = seed.Date.AddYears(-4);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE metro.user_profiles SET birth_date={birth} WHERE user_id={seed.Recipient}");
        var childInput = new BookingSelection(seed.Single, seed.Recipient, seed.Date, seed.Origin, seed.Destination, EligibilityGroup: "CHILD_UNDER_6");
        var create = $"/api/v1/orders?operatorCode={seed.Code}";
        await Expect(await Send(guardian, HttpMethod.Post, create, childInput), HttpStatusCode.Forbidden);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO metro.guardian_links(operator_id,guardian_user_id,child_user_id,relationship,status,verified_by_user_id,verified_at,valid_from,valid_to)
            VALUES ({seed.Op},{seed.Buyer},{seed.Recipient},'PARENT','ACTIVE',{seed.Other},now(),{seed.Date.AddDays(-1)},{seed.Date.AddMonths(1)})
            """);
        await Expect(await Send(stranger, HttpMethod.Post, create, childInput), HttpStatusCode.Forbidden);
        await Expect(await Send(guardian, HttpMethod.Post, create, childInput with { TicketProductId = seed.DayPass, ServiceDate = null,
            OriginStationId = null, DestinationStationId = null }), HttpStatusCode.UnprocessableEntity);
        var child = await Booking(await Send(guardian, HttpMethod.Post, create, childInput), HttpStatusCode.Created);
        Assert.True(child.IsFreeChild);
        Assert.Null(child.ConsentId);
        Assert.Equal(0, child.TotalMinor);
        Assert.Equal(child.SubtotalMinor, child.DiscountMinor);
        var path = $"/api/v1/orders/{child.Id}";
        var confirm = new ConfirmBookingRequest(1, 0);
        await Expect(await Send(guardian, HttpMethod.Post, path + "/confirm", confirm), HttpStatusCode.Conflict);
        var parentInput = childInput with { BeneficiaryUserId = null, EligibilityGroup = "ORDINARY" };
        var parent = await Booking(await Send(guardian, HttpMethod.Post, create, parentInput), HttpStatusCode.Created);
        await Booking(await Send(guardian, HttpMethod.Post, $"/api/v1/orders/{parent.Id}/confirm", new ConfirmBookingRequest(1, parent.TotalMinor)));
        await PayAsync(guardian, parent);
        var key = Guid.NewGuid().ToString();
        var free = await Booking(await Send(guardian, HttpMethod.Post, path + "/confirm", confirm, key));
        Assert.Equal("PENDING_ISSUE", free.Status);
        Assert.Equal(free, await Booking(await Send(guardian, HttpMethod.Post, path + "/confirm", confirm, key)));
        var stored = await db.Orders.IgnoreQueryFilters().AsNoTracking().SingleAsync(o => o.Id == child.Id);
        Assert.NotNull(stored.FreeConfirmedAt);
        Assert.Null(stored.PaymentStartedAt);
        Assert.Null(stored.PaidAt);
        Assert.False(await db.Payments.IgnoreQueryFilters().AnyAsync(p => p.OrderId == child.Id));
        var ticket = await db.Tickets.IgnoreQueryFilters().SingleAsync(t => t.OrderId == child.Id);
        Assert.Equal(seed.Buyer, ticket.GuardianUserId);
        Assert.NotNull(ticket.RelatedTicketId);
        Assert.False(await db.TicketCredentials.IgnoreQueryFilters().AnyAsync(c => c.TicketId == ticket.Id));
        await scope.ServiceProvider.GetRequiredService<BookingSuggestionProcessor>().ProcessAsync(0, default);
        Assert.Equal("SUGGESTED", (await guardian.GetFromJsonAsync<TripSuggestionResponse>(path + "/suggestion"))!.Status);
        await Expect(await guardian.PostAsJsonAsync("/api/v1/payments", new CreatePaymentRequest(child.Id, Guid.NewGuid().ToString(), 1)), HttpStatusCode.Conflict);
    }

    [PostgreSqlFact, Trait("Category", "PostgreSQL")]
    public async Task Two_orders_for_one_beneficiary_cannot_start_payments_concurrently_even_on_different_dates()
    {
        var seed = await SeedAsync();
        using var buyer = await ClientAsync(seed.Buyer);
        var input = new BookingSelection(seed.Single, ServiceDate: seed.Date, OriginStationId: seed.Origin, DestinationStationId: seed.Destination);
        var orders = new List<BookingResponse>();
        foreach (var date in new[] { seed.Date, seed.Date.AddDays(1) })
        {
            var order = await Booking(await Send(buyer, HttpMethod.Post, $"/api/v1/orders?operatorCode={seed.Code}", input with { ServiceDate = date }), HttpStatusCode.Created);
            orders.Add(await Booking(await Send(buyer, HttpMethod.Post, $"/api/v1/orders/{order.Id}/confirm", new ConfirmBookingRequest(1, order.TotalMinor))));
        }
        var replies = await Task.WhenAll(orders.Select(o => buyer.PostAsJsonAsync("/api/v1/payments", new CreatePaymentRequest(o.Id, Guid.NewGuid().ToString(), 1))));
        Assert.Single(replies, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Single(replies, r => r.StatusCode == HttpStatusCode.Conflict);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.Payments.IgnoreQueryFilters().CountAsync(p => p.OperatorId == seed.Op));
    }

    [PostgreSqlFact, Trait("Category", "PostgreSQL")]
    public async Task Expired_consent_or_original_order_deadline_cannot_be_extended_by_a_revision()
    {
        var seed = await SeedAsync();
        using var buyer = await ClientAsync(seed.Buyer);
        using var recipient = await ClientAsync(seed.Recipient);
        var input = new BookingSelection(seed.DayPass, seed.Recipient);
        var order = await Booking(await Send(buyer, HttpMethod.Post, $"/api/v1/orders?operatorCode={seed.Code}", input), HttpStatusCode.Created);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE metro.purchase_consents SET requested_at=now()-interval '25 hours',expires_at=now()-interval '1 hour' WHERE id={order.ConsentId}");
        await Expect(await Send(recipient, HttpMethod.Post, $"/api/v1/orders/{order.Id}/consent", new BookingConsentRequest(1, order.ConsentId!.Value, "ACCEPT")), HttpStatusCode.Conflict);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE metro.orders SET created_at=now()-interval '25 hours' WHERE id={order.Id}");
        await Expect(await Send(buyer, HttpMethod.Put, $"/api/v1/orders/{order.Id}", new ReviseBookingRequest(1, input)), HttpStatusCode.Conflict);
    }

    private static async Task FinalNoChargeAsync(ApplicationDbContext db, Guid payment) =>
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE metro.payments SET status='FAILED',reconciliation_status='MATCHED',verified_at=now() WHERE id={payment}");

    [PostgreSqlFact, Trait("Category", "PostgreSQL")]
    public async Task Provider_transport_failure_keeps_the_durable_attempt_unresolved_and_blocks_another_charge()
    {
        var seed = await SeedAsync();
        using var buyer = await ClientAsync(seed.Buyer);
        var selection = new BookingSelection(seed.DayPass);
        var order = await Booking(await Send(buyer, HttpMethod.Post, $"/api/v1/orders?operatorCode={seed.Code}", selection), HttpStatusCode.Created);
        var path = $"/api/v1/orders/{order.Id}";
        await Booking(await Send(buyer, HttpMethod.Post, path + "/confirm", new ConfirmBookingRequest(1, order.TotalMinor)));
        using var failingFactory = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPaymentGateway>();
            services.AddScoped<IPaymentGateway, TransportFailureGateway>();
        }));
        using var failing = failingFactory.CreateClient();
        failing.DefaultRequestHeaders.Authorization = await BearerAsync(failingFactory, seed.Buyer);
        var request = new CreatePaymentRequest(order.Id, Guid.NewGuid().ToString(), 1);
        var response = await failing.PostAsJsonAsync("/api/v1/payments", request);
        await Expect(response, HttpStatusCode.ServiceUnavailable);
        Assert.Equal("PAYMENT_OUTCOME_UNKNOWN", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        var replay = await buyer.PostAsJsonAsync("/api/v1/payments", request);
        await Expect(replay, HttpStatusCode.Created);
        var pending = (await replay.Content.ReadFromJsonAsync<PaymentResponse>())!;
        Assert.Equal("PENDING", pending.Status);
        await Expect(await buyer.PostAsync($"/api/v1/payments/{pending.Id}/retry", null), HttpStatusCode.Conflict);
        await Expect(await Send(buyer, HttpMethod.Put, path, new ReviseBookingRequest(1, selection)), HttpStatusCode.Conflict);
        await Expect(await Send(buyer, HttpMethod.Delete, path + "?orderVersion=1", null), HttpStatusCode.Conflict);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.Payments.IgnoreQueryFilters().CountAsync(p => p.OrderId == order.Id));
    }

    private sealed class TransportFailureGateway : IPaymentGateway
    {
        public Task<(string ProviderReference, string? CheckoutUrl)> CreateTransactionAsync(string paymentCode, long amountMinor,
            string idempotencyKey, CancellationToken cancellationToken) => throw new HttpRequestException("Transport unavailable");
        public bool VerifySignature(string rawBody, string signature) => false;
    }

    [PostgreSqlFact, Trait("Category", "PostgreSQL")]
    public async Task A_train_still_boarding_at_the_origin_is_considered_without_inventing_a_new_departure_time()
    {
        var seed = await SeedAsync();
        using var buyer = await ClientAsync(seed.Buyer);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTimeOffset.UtcNow;
        var today = seed.Date.AddDays(-1);
        var scheduled = now.AddMinutes(-5);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE metro.service_calendars SET effective_from={today} WHERE operator_id={seed.Op};
            UPDATE metro.planned_trips SET service_date={today},planned_start_at={now.AddMinutes(-10)},planned_end_at={now.AddMinutes(10)}
              WHERE id={seed.FirstTrip};
            UPDATE metro.planned_stop_times SET planned_arrival_at={scheduled},planned_departure_at={scheduled}
              WHERE planned_trip_id={seed.FirstTrip} AND station_id={seed.Origin};
            UPDATE metro.planned_stop_times SET planned_arrival_at={now.AddMinutes(10)},planned_departure_at={now.AddMinutes(10)}
              WHERE planned_trip_id={seed.FirstTrip} AND station_id={seed.Destination};
            INSERT INTO metro.actual_trips(operator_id,planned_trip_id,status,source)
              VALUES ({seed.Op},{seed.FirstTrip},'IN_SERVICE','MANUAL');
            INSERT INTO metro.actual_stop_times(operator_id,actual_trip_id,station_id,sequence_no,actual_arrival_at,source)
              SELECT {seed.Op},id,{seed.Origin},1,{now.AddMinutes(-6)},'MANUAL'
              FROM metro.actual_trips WHERE planned_trip_id={seed.FirstTrip};
            """);
        var input = new BookingSelection(seed.Single, ServiceDate: today, OriginStationId: seed.Origin, DestinationStationId: seed.Destination);
        var order = await Booking(await Send(buyer, HttpMethod.Post, $"/api/v1/orders?operatorCode={seed.Code}", input), HttpStatusCode.Created);
        await Booking(await Send(buyer, HttpMethod.Post, $"/api/v1/orders/{order.Id}/confirm", new ConfirmBookingRequest(1, order.TotalMinor)));
        await PayAsync(buyer, order);
        var processor = scope.ServiceProvider.GetRequiredService<BookingSuggestionProcessor>();
        await processor.ProcessAsync(0, default);
        var hint = (await buyer.GetFromJsonAsync<TripSuggestionResponse>($"/api/v1/orders/{order.Id}/suggestion"))!;
        Assert.Equal("SUGGESTED", hint.Status);
        Assert.Equal(seed.FirstTrip, hint.PlannedTripId);
        Assert.Equal("MANUAL", hint.Source);
        Assert.Equal(scheduled.ToUnixTimeMilliseconds(), hint.DepartureAt!.Value.ToUnixTimeMilliseconds());
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE metro.actual_stop_times SET actual_departure_at=now() WHERE operator_id={seed.Op} AND station_id={seed.Origin}
            """);
        await processor.ProcessAsync(0, default);
        Assert.Equal("NO_TRIP", (await buyer.GetFromJsonAsync<TripSuggestionResponse>($"/api/v1/orders/{order.Id}/suggestion"))!.Status);
    }

    [PostgreSqlFact, Trait("Category", "PostgreSQL")]
    public async Task Confirmed_paid_right_without_a_ticket_blocks_overlap_before_another_charge()
    {
        var seed = await SeedAsync();
        using var buyer = await ClientAsync(seed.Buyer);
        var input = new BookingSelection(seed.DayPass);
        var create = $"/api/v1/orders?operatorCode={seed.Code}";
        var paid = await Booking(await Send(buyer, HttpMethod.Post, create, input), HttpStatusCode.Created);
        await Booking(await Send(buyer, HttpMethod.Post, $"/api/v1/orders/{paid.Id}/confirm", new ConfirmBookingRequest(1, paid.TotalMinor)));
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        // A durable confirmed right must block overlap even if its ticket has not been inserted yet.
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE metro.orders SET status='PAID',paid_at=now() WHERE id={paid.Id}");
        var next = await Booking(await Send(buyer, HttpMethod.Post, create, input), HttpStatusCode.Created);
        var response = await Send(buyer, HttpMethod.Post, $"/api/v1/orders/{next.Id}/confirm", new ConfirmBookingRequest(1, next.TotalMinor));
        await Expect(response, HttpStatusCode.Conflict);
        Assert.Equal("BENEFICIARY_RIGHT_CONFLICT", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.False(await db.Payments.IgnoreQueryFilters().AnyAsync(p => p.OrderId == next.Id));
    }

    [PostgreSqlFact, Trait("Category", "PostgreSQL")]
    public async Task Suggestion_uses_published_ETA_preserves_history_and_stops_after_entry()
    {
        var seed = await SeedAsync();
        using var buyer = await ClientAsync(seed.Buyer);
        var input = new BookingSelection(seed.Single, ServiceDate: seed.Date, OriginStationId: seed.Origin, DestinationStationId: seed.Destination);
        var order = await Booking(await Send(buyer, HttpMethod.Post, $"/api/v1/orders?operatorCode={seed.Code}", input), HttpStatusCode.Created);
        var path = $"/api/v1/orders/{order.Id}";
        await Booking(await Send(buyer, HttpMethod.Post, path + "/confirm", new ConfirmBookingRequest(1, order.TotalMinor)));
        await PayAsync(buyer, order);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var worker = scope.ServiceProvider.GetRequiredService<BookingSuggestionProcessor>();
        await worker.ProcessAsync(0, default);
        var original = (await buyer.GetFromJsonAsync<TripSuggestionResponse>(path + "/suggestion"))!;
        var eta = original.DepartureAt!.Value.AddMinutes(5);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO metro.trip_eta_updates(operator_id,planned_trip_id,station_id,expected_departure_at,recorded_by_user_id)
            VALUES ({seed.Op},{seed.FirstTrip},{seed.Origin},{eta},{seed.Other})
            """);
        await worker.ProcessAsync(0, default);
        var updated = (await buyer.GetFromJsonAsync<TripSuggestionResponse>(path + "/suggestion"))!;
        Assert.Equal(eta, updated.DepartureAt);
        Assert.Equal("ETA", updated.Source);
        Assert.False(updated.HoldsCapacity);
        Assert.Equal(2, await db.TripSuggestions.IgnoreQueryFilters().CountAsync(s => s.OrderId == order.Id));
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE metro.tickets SET first_used_at=now() WHERE order_id={order.Id}");
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE metro.planned_trips SET status='CANCELLED',cancellation_reason='test' WHERE id={seed.FirstTrip}");
        await worker.ProcessAsync(0, default);
        Assert.Equal(updated, await buyer.GetFromJsonAsync<TripSuggestionResponse>(path + "/suggestion"));
        Assert.Equal("PAID", await db.Orders.IgnoreQueryFilters().Where(o => o.Id == order.Id).Select(o => o.Status).SingleAsync());
    }

    private static async Task PayAsync(HttpClient buyer, BookingResponse order)
    {
        var response = await buyer.PostAsJsonAsync("/api/v1/payments", new CreatePaymentRequest(order.Id, Guid.NewGuid().ToString(), order.OrderVersion));
        await Expect(response, HttpStatusCode.Created);
        var payment = (await response.Content.ReadFromJsonAsync<PaymentResponse>())!;
        var payload = JsonSerializer.Serialize(new { eventId = Guid.NewGuid().ToString(), idempotencyKey = Guid.NewGuid().ToString(),
            paymentId = payment.Id.ToString(), amount = payment.AmountMinor, currency = "VND" });
        using var webhook = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/momo") { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
        webhook.Headers.Add("X-MoMo-Signature", Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(StubPaymentGateway.Secret), Encoding.UTF8.GetBytes(payload))));
        await Expect(await buyer.SendAsync(webhook), HttpStatusCode.OK);
    }
}
