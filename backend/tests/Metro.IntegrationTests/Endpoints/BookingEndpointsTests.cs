using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Metro.Application.Abstractions.Booking;
using Metro.Application.Ticketing;
using Metro.Infrastructure.Booking;
using Metro.Infrastructure.Persistence.Context;
using Metro.IntegrationTests.Infrastructure;
using Metro.IntegrationTests.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Metro.IntegrationTests.Payments.PaymentScenarioFixture;

namespace Metro.IntegrationTests.Endpoints;

[Collection(PaymentCollection.Name)]
public sealed partial class BookingEndpointsTests(PaymentTestFactory factory)
{
    [PostgreSqlFact, Trait("Category", "PostgreSQL")]
    public async Task Concurrent_create_replays_and_consent_is_bound_to_the_recipient_and_order_version()
    {
        var seed = await SeedAsync();
        using var buyer = await ClientAsync(seed.Buyer);
        using var recipient = await ClientAsync(seed.Recipient);
        using var stranger = await ClientAsync(seed.Other);
        using var anonymous = factory.CreateClient();
        var create = $"/api/v1/orders?operatorCode={seed.Code}";
        var key = Guid.NewGuid().ToString();
        var input = new BookingSelection(seed.DayPass, seed.Recipient);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(anonymous, HttpMethod.Post, create, input, key)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await buyer.PostAsJsonAsync(create, input)).StatusCode);
        var pair = await Task.WhenAll(Send(buyer, HttpMethod.Post, create, input, key), Send(buyer, HttpMethod.Post, create, input, key));
        var order = await Booking(pair[0], HttpStatusCode.Created);
        Assert.Equal(order, await Booking(pair[1], HttpStatusCode.Created));
        Assert.Equal(HttpStatusCode.Conflict, (await Send(buyer, HttpMethod.Post, create, input with { BeneficiaryUserId = null }, key)).StatusCode);
        var path = $"/api/v1/orders/{order.Id}";
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.GetAsync(path)).StatusCode);
        Assert.Contains(order.Id, (await recipient.GetFromJsonAsync<Guid[]>("/api/v1/orders/consents"))!);
        var accept = new BookingConsentRequest(1, order.ConsentId!.Value, "ACCEPT");
        var confirm = new ConfirmBookingRequest(1, 30000);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(buyer, HttpMethod.Post, path + "/consent", accept)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(buyer, HttpMethod.Post, path + "/confirm", confirm)).StatusCode);
        Assert.Equal("ACCEPTED", (await Booking(await Send(recipient, HttpMethod.Post, path + "/consent", accept))).ConsentStatus);
        Assert.Equal("PENDING_PAYMENT", (await Booking(await Send(buyer, HttpMethod.Post, path + "/confirm", confirm))).Status);
        Assert.Equal("WITHDRAWN", (await Booking(await Send(recipient, HttpMethod.Post, path + "/consent",
            accept with { Decision = "WITHDRAW" }))).ConsentStatus);
        var payment = await buyer.PostAsJsonAsync("/api/v1/payments",
            new { orderId = order.Id, orderVersion = 1, idempotencyKey = Guid.NewGuid().ToString() });
        Assert.Equal(HttpStatusCode.Conflict, payment.StatusCode);
        Assert.Equal("BOOKING_CONSENT_REQUIRED", (await payment.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        var newVersion = await Booking(await Send(buyer, HttpMethod.Put, path, new ReviseBookingRequest(1, input)));
        Assert.Equal(2, newVersion.OrderVersion);
        Assert.NotEqual(order.ConsentId, newVersion.ConsentId);
        Assert.Equal("REQUESTED", newVersion.ConsentStatus);
        Assert.Equal(HttpStatusCode.Conflict, (await Send(recipient, HttpMethod.Post, path + "/consent", accept)).StatusCode);
        Assert.Equal("CANCELLED", (await Booking(await Send(buyer, HttpMethod.Delete, path + "?orderVersion=2", null))).Status);
    }

    [PostgreSqlFact, Trait("Category", "PostgreSQL")]
    public async Task Verified_payment_gets_one_future_trip_and_refresh_preserves_history_without_holds()
    {
        var seed = await SeedAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var worker = scope.ServiceProvider.GetRequiredService<BookingSuggestionProcessor>();
        using var buyer = await ClientAsync(seed.Buyer);
        var input = new BookingSelection(seed.Single, ServiceDate: seed.Date,
            OriginStationId: seed.Origin, DestinationStationId: seed.Destination);
        var order = await Booking(await Send(buyer, HttpMethod.Post, $"/api/v1/orders?operatorCode={seed.Code}", input), HttpStatusCode.Created);
        Assert.Equal(5000, order.TotalMinor);
        var path = $"/api/v1/orders/{order.Id}";
        await Booking(await Send(buyer, HttpMethod.Post, path + "/confirm", new ConfirmBookingRequest(1, order.TotalMinor)));
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE metro.fare_matrix_entries SET amount_minor=6000 WHERE operator_id={seed.Op} AND ticket_product_id={seed.Single}
            """);
        var paymentInput = new { orderId = order.Id, idempotencyKey = Guid.NewGuid().ToString(), orderVersion = 1 };
        var stale = await buyer.PostAsJsonAsync("/api/v1/payments", paymentInput);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("CHECKOUT_PRICE_OR_ROUTE_CHANGED", (await stale.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        var newOrder = await Booking(await Send(buyer, HttpMethod.Put, path, new ReviseBookingRequest(1, input)));
        Assert.Equal(6000, newOrder.TotalMinor);
        await Booking(await Send(buyer, HttpMethod.Post, path + "/confirm", new ConfirmBookingRequest(2, 6000)));
        Assert.Equal(HttpStatusCode.Conflict, (await buyer.PostAsJsonAsync("/api/v1/payments", paymentInput)).StatusCode);
        var paymentResponse = await buyer.PostAsJsonAsync("/api/v1/payments", paymentInput with { orderVersion = 2 });
        await Expect(paymentResponse, HttpStatusCode.Created);
        var payment = (await paymentResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Conflict, (await Send(buyer, HttpMethod.Delete, path + "?orderVersion=2", null)).StatusCode);
        Assert.Equal("NOT_AVAILABLE", (await buyer.GetFromJsonAsync<TripSuggestionResponse>(path + "/suggestion"))!.Status);
        var payload = JsonSerializer.Serialize(new
        {
            eventId = Guid.NewGuid().ToString(), idempotencyKey = Guid.NewGuid().ToString(), paymentId = payment.ToString(),
            amount = 6000, currency = "VND"
        });
        using var webhook = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/momo") { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
        webhook.Headers.Add("X-MoMo-Signature", Convert.ToHexStringLower(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(StubPaymentGateway.Secret), Encoding.UTF8.GetBytes(payload))));
        await Expect(await buyer.SendAsync(webhook), HttpStatusCode.OK);
        Assert.Equal("PENDING", (await buyer.GetFromJsonAsync<TripSuggestionResponse>(path + "/suggestion"))!.Status);
        await worker.ProcessAsync(0, default);
        await worker.ProcessAsync(0, default);
        var suggestion = (await buyer.GetFromJsonAsync<TripSuggestionResponse>(path + "/suggestion"))!;
        Assert.Equal("SUGGESTED", suggestion.Status);
        Assert.Equal(seed.FirstTrip, suggestion.PlannedTripId);
        Assert.False(suggestion.HoldsCapacity);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE metro.planned_trips SET status='CANCELLED', cancellation_reason='test' WHERE operator_id={seed.Op}
            """);
        await worker.ProcessAsync(0, default);
        await worker.ProcessAsync(0, default);
        Assert.Equal("NO_TRIP", (await buyer.GetFromJsonAsync<TripSuggestionResponse>(path + "/suggestion"))!.Status);
        Assert.Equal(2, await db.TripSuggestions.IgnoreQueryFilters().CountAsync(s => s.OrderId == order.Id));
        Assert.Equal(1, await db.TripSuggestions.IgnoreQueryFilters().CountAsync(s => s.OrderId == order.Id && s.SupersededAt == null));
        Assert.Null((await db.OrderItems.IgnoreQueryFilters().SingleAsync(i => i.OrderId == order.Id)).PlannedTripId);
    }

    [PostgreSqlFact, Trait("Category", "PostgreSQL")]
    public async Task Discount_uses_the_recipient_and_revocation_requires_a_new_confirmed_price()
    {
        var seed = await SeedAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        using var buyer = await ClientAsync(seed.Buyer);
        using var beneficiary = await ClientAsync(seed.Recipient);
        var today = seed.Date.AddDays(-1);
        await VerifyEvidenceAsync(db, seed.Buyer, seed.Other, "STUDENT", today);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO metro.fare_matrix_entries
                (operator_id,fare_policy_version_id,ticket_product_id,payment_channel,eligibility_group,amount_minor)
            SELECT operator_id,fare_policy_version_id,ticket_product_id,payment_channel,'STUDENT',25000
            FROM metro.fare_matrix_entries WHERE operator_id={seed.Op} AND ticket_product_id={seed.DayPass}
            """);
        var selection = new BookingSelection(seed.DayPass, seed.Recipient, EligibilityGroup: "STUDENT");
        var order = await Booking(await Send(buyer, HttpMethod.Post, $"/api/v1/orders?operatorCode={seed.Code}", selection), HttpStatusCode.Created);
        Assert.True(order.FallbackToOrdinary);
        Assert.Equal(30_000, order.TotalMinor);

        var evidence = await VerifyEvidenceAsync(db, seed.Recipient, seed.Other, "STUDENT", today);
        var path = $"/api/v1/orders/{order.Id}";
        var discounted = await Booking(await Send(buyer, HttpMethod.Put, path, new ReviseBookingRequest(1, selection)));
        Assert.Equal("STUDENT", discounted.AppliedEligibilityGroup);
        Assert.Equal(30_000, discounted.SubtotalMinor);
        Assert.Equal(3_000, discounted.DiscountMinor);
        Assert.Equal(27_000, discounted.TotalMinor);
        var snapshot = await db.OrderVersions.IgnoreQueryFilters().Where(v => v.OrderId == order.Id && v.Version == 2)
            .Select(v => v.FareSnapshot).SingleAsync();
        using var snapshotJson = JsonDocument.Parse(snapshot);
        Assert.Equal(evidence, snapshotJson.RootElement.GetProperty("EvidenceId").GetGuid());
        await Booking(await Send(beneficiary, HttpMethod.Post, path + "/consent",
            new BookingConsentRequest(2, discounted.ConsentId!.Value, "ACCEPT")));
        await Booking(await Send(buyer, HttpMethod.Post, path + "/confirm", new ConfirmBookingRequest(2, 27_000)));

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE metro.profile_eligibility_requests SET status='REJECTED' WHERE id={evidence}
            """);
        var blocked = await buyer.PostAsJsonAsync("/api/v1/payments",
            new { orderId = order.Id, orderVersion = 2, idempotencyKey = Guid.NewGuid().ToString() });
        await Expect(blocked, HttpStatusCode.Conflict);
        Assert.Equal("CHECKOUT_PRICE_OR_ROUTE_CHANGED", (await blocked.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.False(await db.Payments.IgnoreQueryFilters().AnyAsync(p => p.OrderId == order.Id));
        var regular = await Booking(await Send(buyer, HttpMethod.Put, path, new ReviseBookingRequest(2, selection)));
        Assert.True(regular.FallbackToOrdinary);
        Assert.Equal(30_000, regular.TotalMinor);
        Assert.NotEqual(discounted.ConsentId, regular.ConsentId);
        Assert.Equal(27_000, await db.OrderVersions.IgnoreQueryFilters().Where(v => v.OrderId == order.Id && v.Version == 2)
            .Select(v => v.TotalMinor).SingleAsync());
    }

    [PostgreSqlFact, Trait("Category", "PostgreSQL")]
    public async Task Elderly_age_is_checked_on_travel_date_for_single_trips_and_payment_day_for_passes()
    {
        var seed = await SeedAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        using var buyer = await ClientAsync(seed.Buyer);
        var today = seed.Date.AddDays(-1);
        var birth = seed.Date.AddYears(-60);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE metro.user_profiles SET birth_date={birth} WHERE user_id={seed.Buyer}");
        await VerifyEvidenceAsync(db, seed.Buyer, seed.Other, "ELDERLY", today);
        var create = $"/api/v1/orders?operatorCode={seed.Code}";
        var pass = await Booking(await Send(buyer, HttpMethod.Post, create,
            new BookingSelection(seed.DayPass, EligibilityGroup: "ELDERLY")), HttpStatusCode.Created);
        Assert.True(pass.FallbackToOrdinary);
        Assert.Equal(today, pass.AgeReferenceDate);
        var beforeBirthday = await Booking(await Send(buyer, HttpMethod.Post, create,
            new BookingSelection(seed.Single, ServiceDate: today, OriginStationId: seed.Origin,
                DestinationStationId: seed.Destination, EligibilityGroup: "ELDERLY")), HttpStatusCode.Created);
        Assert.Equal(5_000, beforeBirthday.TotalMinor);
        Assert.True(beforeBirthday.FallbackToOrdinary);
        var onBirthday = await Booking(await Send(buyer, HttpMethod.Post, create,
            new BookingSelection(seed.Single, ServiceDate: seed.Date, OriginStationId: seed.Origin,
                DestinationStationId: seed.Destination, EligibilityGroup: "ELDERLY")), HttpStatusCode.Created);
        Assert.Equal(seed.Date, onBirthday.AgeReferenceDate);
        Assert.Equal("ELDERLY", onBirthday.AppliedEligibilityGroup);
        Assert.Equal(4_000, onBirthday.TotalMinor);
        await Booking(await Send(buyer, HttpMethod.Post, $"/api/v1/orders/{onBirthday.Id}/confirm",
            new ConfirmBookingRequest(1, 4_000)));
        var payment = await buyer.PostAsJsonAsync("/api/v1/payments",
            new { orderId = onBirthday.Id, orderVersion = 1, idempotencyKey = Guid.NewGuid().ToString() });
        await Expect(payment, HttpStatusCode.Created);
        Assert.Equal(4_000, (await payment.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("amountMinor").GetInt64());
    }

    private static async Task<Guid> VerifyEvidenceAsync(ApplicationDbContext db, string beneficiary, string reviewer,
        string category, DateOnly from)
    {
        var id = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO metro.profile_eligibility_requests
              (id,user_id,category,status,valid_from,valid_to,decided_by_user_id,decided_at)
            VALUES ({id},{beneficiary},{category},'VERIFIED',{from},{from.AddMonths(6)},{reviewer},now())
            """);
        return id;
    }

    private async Task<HttpClient> ClientAsync(string user)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = await BearerAsync(factory, user);
        return client;
    }

    private static async Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string path, object? body, string? key = null)
    {
        using var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString());
        return await client.SendAsync(request);
    }

    private static async Task Expect(HttpResponseMessage response, HttpStatusCode status) =>
        Assert.True(response.StatusCode == status, $"Expected {status}, got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

    private static async Task<BookingResponse> Booking(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK)
    {
        await Expect(response, status);
        return (await response.Content.ReadFromJsonAsync<BookingResponse>())!;
    }

    private async Task<Seed> SeedAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var buyer = await UserAsync(scope.ServiceProvider);
        var recipient = await UserAsync(scope.ServiceProvider);
        var other = await UserAsync(scope.ServiceProvider);
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var op = Guid.NewGuid();
        var code = $"BE07-{op:N}";
        var fare = Guid.NewGuid();
        var dayPass = Guid.NewGuid();
        var single = Guid.NewGuid();
        var line = Guid.NewGuid();
        var origin = Guid.NewGuid();
        var destination = Guid.NewGuid();
        var calendar = Guid.NewGuid();
        var schedule = Guid.NewGuid();
        var trip = Guid.NewGuid();
        var next = Guid.NewGuid();
        var date = TicketRules.LocalDate(DateTimeOffset.UtcNow).AddDays(1);
        var starts = TicketRules.Midnight(date).AddHours(6);
        var effective = DateTime.UtcNow.AddDays(-1);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO metro.operators(id,code,name) VALUES ({op},{code},'BE07 test');
            INSERT INTO metro.fare_policy_versions (id,operator_id,code,version,scope_key,status,effective_from,
                created_by_user_id,approved_by_user_id,approved_at,published_by_user_id,published_at)
                VALUES ({fare},{op},'V1',1,'DEFAULT','PUBLISHED',{effective},{buyer},{other},{effective},{other},{effective});
            INSERT INTO metro.ticket_products (id,operator_id,code,name,product_type,duration_value,duration_unit,expiry_rule)
                VALUES ({dayPass},{op},'DAY','Day','DAY',1,'DAY','NEXT_LOCAL_MIDNIGHT'),
                       ({single},{op},'SINGLE','Single','SINGLE_TRIP',NULL,NULL,'SINGLE_JOURNEY');
            INSERT INTO metro.fare_matrix_entries(operator_id,fare_policy_version_id,ticket_product_id,payment_channel,amount_minor)
                VALUES ({op},{fare},{dayPass},'ANY',30000),({op},{fare},{single},'ANY',5000);
            INSERT INTO metro.lines (id,operator_id,code,name) VALUES ({line},{op},'L1','Line');
            INSERT INTO metro.stations (id,operator_id,code,name) VALUES ({origin},{op},'S1','Origin'),({destination},{op},'S2','Destination');
            INSERT INTO metro.line_stations (operator_id,line_id,station_id,direction,sequence_no,distance_from_origin_m)
                VALUES ({op},{line},{origin},0,1,0),({op},{line},{destination},0,2,500);
            INSERT INTO metro.service_calendars (id,operator_id,code,name,day_type,effective_from,status)
                VALUES ({calendar},{op},'CAL','Calendar','WEEKDAY',{date},'PUBLISHED');
            INSERT INTO metro.schedule_versions (id,operator_id,line_id,service_calendar_id,version,code,status,effective_from,
                 created_by_user_id,approved_by_user_id,approved_at,published_by_user_id,published_at)
                VALUES ({schedule},{op},{line},{calendar},1,'SCH','PUBLISHED',{effective},{buyer},{other},{effective},{other},{effective});
            INSERT INTO metro.planned_trips (id,operator_id,schedule_version_id,line_id,trip_code,service_date,direction,planned_start_at,planned_end_at)
                VALUES ({trip},{op},{schedule},{line},'FIRST',{date},0,{starts},{starts.AddMinutes(10)}),
                       ({next},{op},{schedule},{line},'LATER',{date},0,{starts.AddMinutes(30)},{starts.AddMinutes(40)});
            INSERT INTO metro.planned_stop_times (operator_id,planned_trip_id,station_id,sequence_no,planned_arrival_at,planned_departure_at)
                VALUES ({op},{trip},{origin},1,{starts},{starts}),({op},{trip},{destination},2,{starts.AddMinutes(10)},{starts.AddMinutes(10)}),
                       ({op},{next},{origin},1,{starts.AddMinutes(30)},{starts.AddMinutes(30)}),
                       ({op},{next},{destination},2,{starts.AddMinutes(40)},{starts.AddMinutes(40)});
            """);
        return new(op, code, buyer, recipient, other, dayPass, single, origin, destination, date, trip);
    }

    private sealed record Seed(Guid Op, string Code, string Buyer, string Recipient, string Other,
        Guid DayPass, Guid Single, Guid Origin, Guid Destination, DateOnly Date, Guid FirstTrip);
}
