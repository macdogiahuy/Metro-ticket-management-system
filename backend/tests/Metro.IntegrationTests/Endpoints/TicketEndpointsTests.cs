using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Metro.Application.Ticketing;
using Metro.Domain.Entities.Identity;
using Metro.Infrastructure.Persistence.Context;
using Metro.Infrastructure.Ticketing;
using Metro.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Metro.IntegrationTests.Endpoints;

[Collection(DatabaseIntegrationCollection.Name)]
public sealed class TicketEndpointsTests(MetroWebApplicationFactory factory)
{
    private static DateOnly Today => TicketRules.LocalDate(DateTimeOffset.UtcNow);
    private static async Task<NpgsqlConnection> Connection()
    {
        var c = new NpgsqlConnection(PostgreSqlTestConfiguration.LoadRequired().ConnectionString);
        await c.OpenAsync(); return c;
    }
    private static async Task<object?> Sql(string sql, params (string Name, object Value)[] parameters)
    {
        await using var c = await Connection(); await using var cmd = new NpgsqlCommand(sql, c);
        foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value);
        return await cmd.ExecuteScalarAsync();
    }
    private async Task<Account> AccountAsync(DateOnly? birth = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var user = new IdentityUser { UserName = $"be09-{Guid.NewGuid():N}@example.invalid", EmailConfirmed = true };
        user.Email = user.UserName; var password = $"Metro!{Guid.NewGuid():N}Aa1";
        Assert.True((await users.CreateAsync(user, password)).Succeeded);
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.UserProfiles.Add(new UserProfile { UserId = user.Id, FullName = "Ticket Test", NormalizedFullName = "TICKET TEST", BirthDate = birth ?? new DateOnly(1990, 1, 1), Status = "ACTIVE" });
        await db.SaveChangesAsync();
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = user.Email, password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return new(user.Id, client);
    }
    private static async Task<Network> NetworkAsync(string product = "SINGLE_TRIP")
    {
        var n = new Network(Guid.NewGuid(), "B09" + Guid.NewGuid().ToString("N")[..12], Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await Sql("""
            INSERT INTO metro.operators(id,code,name) VALUES(@op,@code,'BE09 test');
            INSERT INTO metro.stations(id,operator_id,code,name) VALUES(@a,@op,'A','Origin'),(@b,@op,'B','Destination');
            INSERT INTO metro.ticket_products(id,operator_id,code,name,product_type,expiry_rule,duration_value,duration_unit) VALUES(@product,@op,'TEST','Test product',@type,CASE WHEN @type='SINGLE_TRIP' THEN 'SINGLE_JOURNEY' ELSE 'NEXT_LOCAL_MIDNIGHT' END,CASE @type WHEN 'DAY' THEN 1 WHEN 'WEEK' THEN 7 WHEN 'MONTH' THEN 30 END,CASE WHEN @type='SINGLE_TRIP' THEN NULL ELSE 'DAY' END);
            INSERT INTO metro.fare_policy_versions(id,operator_id,code,version,scope_key,effective_from,created_by_user_id) VALUES(@fare,@op,'TEST',1,'TEST',clock_timestamp()-interval '1 day',(SELECT "Id" FROM public."AspNetUsers" ORDER BY "Id" LIMIT 1));
            """, ("op", n.Op), ("code", n.Code), ("a", n.A), ("b", n.B), ("product", n.Product), ("type", product), ("fare", n.Fare));
        return n;
    }
    private static async Task<Guid> OrderAsync(Network n, string holder, string? buyer = null, bool free = false, DateOnly? day = null)
    {
        var id = Guid.NewGuid();
        await Sql("""
            INSERT INTO metro.orders(id,operator_id,order_code,channel,payer_user_id,beneficiary_user_id,subtotal_minor,discount_minor,total_minor,fare_policy_version_id,service_date)
             VALUES(@id,@op,@code,'WEB',@buyer,@holder,10000,@discount,@total,@fare,@day);
            INSERT INTO metro.order_items(operator_id,order_id,ticket_product_id,origin_station_id,destination_station_id,unit_price_minor,discount_minor,line_total_minor)
             VALUES(@op,@id,@product,@a,@b,10000,@discount,@total);
            INSERT INTO metro.order_versions(operator_id,order_id,version,status,ticket_product_id,beneficiary_user_id,service_date,origin_station_id,destination_station_id,subtotal_minor,discount_minor,total_minor,fare_policy_version_id,confirmed_at)
             VALUES(@op,@id,1,'CONFIRMED',@product,@holder,@day,@a,@b,10000,@discount,@total,@fare,clock_timestamp());
            UPDATE metro.orders SET status='HELD' WHERE id=@id;
            """, ("id", id), ("op", n.Op), ("code", "O-" + id.ToString("N")), ("buyer", buyer ?? holder), ("holder", holder),
            ("discount", free ? 10000L : 0L), ("total", free ? 0L : 10000L), ("fare", n.Fare), ("day", day ?? Today), ("product", n.Product), ("a", n.A), ("b", n.B));
        return id;
    }
    private static async Task<Guid> PaymentAsync(Network n, Guid order)
    {
        var id = Guid.NewGuid();
        await Sql("""
            INSERT INTO metro.payments(id,operator_id,order_id,payment_code,provider,method,amount_minor,idempotency_key)
             VALUES(@id,@op,@order,@code,'BE09_TEST','SANDBOX_GATEWAY',10000,@code)
            """, ("id", id), ("op", n.Op), ("order", order), ("code", id.ToString("N")));
        return id;
    }
    private static Task<object?> Confirm(Network n, Guid payment, string? eventId = null, bool verified = true, string? payloadHash = null) =>
        Sql("SELECT metro.confirm_payment_and_issue_tickets(@op,@payment,@event,@key,@hash,10000::bigint,'VND'::char(3),@verified)",
            ("op", n.Op), ("payment", payment), ("event", eventId ?? payment.ToString()), ("key", payment.ToString()), ("hash", payloadHash ?? new string('a', 64)), ("verified", verified));
    private async Task ProcessAsync(TicketQrCodec? codec = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var processor = codec is null ? scope.ServiceProvider.GetRequiredService<TicketIssuanceProcessor>() :
            new TicketIssuanceProcessor(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(), codec);
        await processor.ProcessDueAsync(500);
    }
    private static async Task<Guid> TicketId(Guid order) => (Guid)(await Sql("SELECT id FROM metro.tickets WHERE order_id=@id", ("id", order)))!;
    private static string Url(Network n, string path) => $"/api/v1/{path}?operatorCode={n.Code}";
    private static HttpRequestMessage FreeRequest(Network n, Guid order, string key, int version = 1)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Url(n, $"orders/{order}/issue-free")) { Content = JsonContent.Create(new { orderVersion = version }) };
        request.Headers.Add("Idempotency-Key", key); return request;
    }
    private static async Task GuardianAsync(Network n, string guardian, string child) =>
        _ = await Sql("""
            INSERT INTO metro.guardian_links(operator_id,guardian_user_id,child_user_id,relationship,status,verified_by_user_id,verified_at,valid_from,valid_to)
             VALUES(@op,@guardian,@child,'PARENT','ACTIVE',@guardian,clock_timestamp(),@day,@end)
            """, ("op", n.Op), ("guardian", guardian), ("child", child), ("day", Today), ("end", Today.AddDays(30)));

    [PostgreSqlFact, Trait("Category", "PostgreSQL")]
    public async Task All_six_routes_require_authentication()
    {
        using var client = factory.CreateClient(); var id = Guid.NewGuid();
        foreach (var path in new[] { "tickets", $"tickets/{id}", $"tickets/{id}/qr", "orders", "payments" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/" + path)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync($"/api/v1/orders/{id}/issue-free", new { orderVersion = 1 })).StatusCode);
    }
    [PostgreSqlFact, Trait("Category", "PostgreSQL")]
    public async Task Paid_SQL_is_idempotent_and_worker_creates_stable_private_QR()
    {
        using var holder = await AccountAsync(); using var buyer = await AccountAsync();
        var n = await NetworkAsync(); var order = await OrderAsync(n, holder.Id, buyer.Id); var payment = await PaymentAsync(n, order);
        Assert.Equal(1, await Confirm(n, payment)); Assert.Equal(1, await Confirm(n, payment));
        Assert.Equal("PAID", await Sql("SELECT status FROM metro.orders WHERE id=@id", ("id", order)));
        var id = await TicketId(order);
        Assert.Equal(HttpStatusCode.Conflict, (await holder.Client.GetAsync(Url(n, $"tickets/{id}/qr"))).StatusCode);
        await ProcessAsync();
        var first = await holder.Client.GetAsync(Url(n, $"tickets/{id}/qr"));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("image/png", first.Content.Headers.ContentType?.MediaType); Assert.True(first.Headers.CacheControl?.NoStore);
        var bytes = await first.Content.ReadAsByteArrayAsync(); Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, bytes[..8]);
        Assert.Equal(bytes, await holder.Client.GetByteArrayAsync(Url(n, $"tickets/{id}/qr")));
        Assert.Equal(HttpStatusCode.NotFound, (await buyer.Client.GetAsync(Url(n, $"tickets/{id}/qr"))).StatusCode);
        Assert.Equal(1L, await Sql("SELECT count(*) FROM metro.ticket_credentials WHERE ticket_id=@id", ("id", id)));
        Assert.Equal("ISSUED", await Sql("SELECT status FROM metro.orders WHERE id=@id", ("id", order)));
        var orders = await buyer.Client.GetFromJsonAsync<JsonElement>(Url(n, "orders")); Assert.Equal(1, orders.GetProperty("total").GetInt32());
        var payments = await buyer.Client.GetFromJsonAsync<JsonElement>(Url(n, "payments")); Assert.Equal(1, payments.GetProperty("total").GetInt32());
        var detail = await holder.Client.GetStringAsync(Url(n, $"tickets/{id}")); Assert.DoesNotContain("tokenHash", detail); Assert.DoesNotContain("holderUserId", detail);
        Assert.Equal(0, (await buyer.Client.GetFromJsonAsync<JsonElement>(Url(n, "tickets"))).GetProperty("total").GetInt32());
        // Historical ticket details read the frozen order version, not subsequently edited order fields.
        await Sql("UPDATE metro.orders SET eligibility_group='LATER_EDIT' WHERE id=@id", ("id", order));
        var frozenDetail = await holder.Client.GetFromJsonAsync<JsonElement>(Url(n, $"tickets/{id}"));
        Assert.Equal(JsonValueKind.Null, frozenDetail.GetProperty("ticket").GetProperty("eligibilityGroup").ValueKind);
    }
    [PostgreSqlFact, Trait("Category", "PostgreSQL")]
    public async Task Future_single_has_no_QR_before_service_day()
    {
        using var holder = await AccountAsync(); var n = await NetworkAsync();
        var order = await OrderAsync(n, holder.Id, day: Today.AddDays(1)); await Confirm(n, await PaymentAsync(n, order)); await ProcessAsync();
        var id = await TicketId(order);
        Assert.Equal(HttpStatusCode.Conflict, (await holder.Client.GetAsync(Url(n, $"tickets/{id}/qr"))).StatusCode);
        Assert.Equal(0L, await Sql("SELECT count(*) FROM metro.ticket_credentials WHERE ticket_id=@id", ("id", id)));
        Assert.Equal(TicketRules.Midnight(Today.AddDays(1)), await Sql("SELECT effective_from FROM metro.tickets WHERE id=@id", ("id", id)));
    }
    [PostgreSqlFact, Trait("Category", "PostgreSQL")]
    public async Task Free_child_requires_guardian_and_no_payment_then_replays_without_duplication()
    {
        using var guardian = await AccountAsync(); using var child = await AccountAsync(Today.AddYears(-5)); using var stranger = await AccountAsync();
        var n = await NetworkAsync(); var parentOrder = await OrderAsync(n, guardian.Id); await Confirm(n, await PaymentAsync(n, parentOrder)); await ProcessAsync();
        await GuardianAsync(n, guardian.Id, child.Id); var order = await OrderAsync(n, child.Id, guardian.Id, true);
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.Client.SendAsync(FreeRequest(n, order, "forbidden-key"))).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await guardian.Client.SendAsync(FreeRequest(n, order, "free-child-key"))).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await guardian.Client.SendAsync(FreeRequest(n, order, "free-child-key"))).StatusCode);
        Assert.Equal(0L, await Sql("SELECT count(*) FROM metro.payments WHERE order_id=@id", ("id", order)));
        Assert.Equal(1L, await Sql("SELECT count(*) FROM metro.tickets WHERE order_id=@id", ("id", order)));
        Assert.Equal(DBNull.Value, await Sql("SELECT paid_at FROM metro.orders WHERE id=@id", ("id", order)));
        await ProcessAsync(); var id = await TicketId(order);
        Assert.Equal(HttpStatusCode.OK, (await guardian.Client.GetAsync(Url(n, $"tickets/{id}/qr"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await child.Client.GetAsync(Url(n, $"tickets/{id}/qr"))).StatusCode);
        var other = await OrderAsync(n, child.Id, guardian.Id, true, Today.AddDays(1));
        Assert.Equal(HttpStatusCode.Conflict, (await guardian.Client.SendAsync(FreeRequest(n, other, "free-child-key"))).StatusCode);
        await Sql("UPDATE metro.guardian_links SET status='REVOKED',revocation_reason='Test revoke' WHERE operator_id=@op AND child_user_id=@child", ("op", n.Op), ("child", child.Id));
        Assert.Equal(HttpStatusCode.NotFound, (await guardian.Client.GetAsync(Url(n, $"tickets/{id}/qr"))).StatusCode);
    }
    [PostgreSqlFact, Trait("Category", "PostgreSQL")]
    public async Task Sixth_birthday_and_wrong_version_are_rejected()
    {
        using var child = await AccountAsync(Today.AddYears(-6)); using var guardian = await AccountAsync();
        var n = await NetworkAsync(); var order = await OrderAsync(n, child.Id, free: true); await GuardianAsync(n, guardian.Id, child.Id);
        Assert.Equal(HttpStatusCode.Conflict, (await guardian.Client.SendAsync(FreeRequest(n, order, "sixth-birthday"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await guardian.Client.SendAsync(FreeRequest(n, order, "version-mismatch", 2))).StatusCode);
        Assert.Equal(0L, await Sql("SELECT count(*) FROM metro.tickets WHERE order_id=@id", ("id", order)));
    }
    [PostgreSqlFact, Trait("Category", "PostgreSQL")]
    public async Task Concurrent_paid_sources_cannot_grant_overlapping_rights()
    {
        using var holder = await AccountAsync(); var n = await NetworkAsync();
        var orders = new[] { await OrderAsync(n, holder.Id), await OrderAsync(n, holder.Id) };
        var payments = new[] { await PaymentAsync(n, orders[0]), await PaymentAsync(n, orders[1]) };
        async Task<bool> Try(Guid p) { try { await Confirm(n, p); return true; } catch (PostgresException e) when (e.SqlState == "23505") { return false; } }
        var results = await Task.WhenAll(payments.Select(Try)); Assert.Single(results, r => r);
        Assert.Equal(1L, await Sql("SELECT count(*) FROM metro.tickets WHERE operator_id=@op", ("op", n.Op)));
    }
    [PostgreSqlFact, Trait("Category", "PostgreSQL")]
    public async Task Version_freeze_and_signature_are_enforced_inside_SQL()
    {
        using var holder = await AccountAsync(); var n = await NetworkAsync(); var order = await OrderAsync(n, holder.Id); var payment = await PaymentAsync(n, order);
        var signature = await Assert.ThrowsAsync<PostgresException>(() => Confirm(n, payment, verified: false)); Assert.Equal("28000", signature.SqlState);
        var frozen = await Assert.ThrowsAsync<PostgresException>(() => Sql("UPDATE metro.order_versions SET fare_snapshot='{\"changed\":true}'::jsonb WHERE order_id=@id", ("id", order)));
        Assert.Equal("22023", frozen.SqlState);
        await Sql("UPDATE metro.orders SET order_version=2 WHERE id=@id", ("id", order));
        var version = await Assert.ThrowsAsync<PostgresException>(() => Confirm(n, payment)); Assert.Equal("22023", version.SqlState);
        Assert.Equal(0L, await Sql("SELECT count(*) FROM metro.tickets WHERE order_id=@id", ("id", order)));
    }
    [PostgreSqlFact, Trait("Category", "PostgreSQL")]
    public async Task Missing_QR_key_retries_and_escalates_without_losing_paid_ticket()
    {
        using var holder = await AccountAsync(); var n = await NetworkAsync(); var order = await OrderAsync(n, holder.Id); await Confirm(n, await PaymentAsync(n, order));
        var id = await TicketId(order); await ProcessAsync(new TicketQrCodec(Options.Create(new TicketQrOptions())));
        Assert.Equal("QR_KEY_UNAVAILABLE", await Sql("SELECT last_error_code FROM metro.ticket_issuance_jobs WHERE ticket_id=@id", ("id", id)));
        await Sql("UPDATE metro.ticket_issuance_jobs SET first_failed_at=clock_timestamp()-interval '15 minutes',next_attempt_at=clock_timestamp() WHERE ticket_id=@id", ("id", id));
        await ProcessAsync(new TicketQrCodec(Options.Create(new TicketQrOptions())));
        Assert.Equal("NEEDS_SUPPORT", await Sql("SELECT status FROM metro.ticket_issuance_jobs WHERE ticket_id=@id", ("id", id)));
        Assert.Equal(2L, await Sql("SELECT count(*) FROM metro.notifications WHERE notification_type='TICKET_NEEDS_SUPPORT' AND content_reference->>'ticketId'=@id", ("id", id.ToString())));
        Assert.Equal("PAID", await Sql("SELECT status FROM metro.orders WHERE id=@id", ("id", order)));
        Assert.Equal(1L, await Sql("SELECT count(*) FROM metro.tickets WHERE order_id=@id", ("id", order)));
    }
    [PostgreSqlFact, Trait("Category", "PostgreSQL")]
    public async Task Consecutive_dates_are_allowed_but_expired_open_journey_blocks_new_right()
    {
        using var holder = await AccountAsync(); var n = await NetworkAsync();
        var first = await OrderAsync(n, holder.Id); await Confirm(n, await PaymentAsync(n, first)); await ProcessAsync();
        var future = await OrderAsync(n, holder.Id, day: Today.AddDays(1)); await Confirm(n, await PaymentAsync(n, future));
        var id = await TicketId(first);
        await Sql("""
            UPDATE metro.tickets SET status='IN_USE',effective_from=clock_timestamp()-interval '2 days',expires_at=clock_timestamp()-interval '1 day' WHERE id=@id;
            INSERT INTO metro.journeys(operator_id,journey_code,ticket_id,status,entered_at) VALUES(@op,@code,@id,'OPEN',clock_timestamp()-interval '2 days');
            """, ("op", n.Op), ("code", Guid.NewGuid().ToString()), ("id", id));
        // An expired ticket must still be retrievable for a legitimate exit; this does not authorize a new entry.
        Assert.Equal(HttpStatusCode.OK, (await holder.Client.GetAsync(Url(n, $"tickets/{id}/qr"))).StatusCode);
        var next = await OrderAsync(n, holder.Id, day: Today.AddDays(2));
        var nextPayment = await PaymentAsync(n, next);
        var error = await Assert.ThrowsAsync<PostgresException>(() => Confirm(n, nextPayment));
        Assert.Equal("23505", error.SqlState);
    }
    [PostgreSqlFact, Trait("Category", "PostgreSQL")]
    public async Task Two_callbacks_and_two_workers_keep_one_ticket_and_current_credential()
    {
        using var holder = await AccountAsync(); var n = await NetworkAsync(); var order = await OrderAsync(n, holder.Id); var payment = await PaymentAsync(n, order);
        var results = await Task.WhenAll(Confirm(n, payment), Confirm(n, payment)); Assert.All(results, r => Assert.Equal(1, r));
        await Task.WhenAll(ProcessAsync(), ProcessAsync()); var id = await TicketId(order);
        Assert.Equal(1L, await Sql("SELECT count(*) FROM metro.tickets WHERE order_id=@id", ("id", order)));
        Assert.Equal(1L, await Sql("SELECT count(*) FROM metro.ticket_credentials WHERE ticket_id=@id AND status='ACTIVE'", ("id", id)));
    }
    [PostgreSqlFact, Trait("Category", "PostgreSQL")]
    public async Task History_pagination_cross_operator_and_invalid_keys_are_protected()
    {
        using var holder = await AccountAsync(); var n = await NetworkAsync(); var other = await NetworkAsync();
        var order = await OrderAsync(n, holder.Id); await Confirm(n, await PaymentAsync(n, order)); await ProcessAsync(); var id = await TicketId(order);
        Assert.Equal(HttpStatusCode.NotFound, (await holder.Client.GetAsync(Url(other, $"tickets/{id}"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await holder.Client.GetAsync(Url(n, "tickets") + "&pageSize=101")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await holder.Client.GetAsync(Url(n, "orders") + "&fromDate=2026-12-02&toDate=2026-11-02")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await holder.Client.PostAsJsonAsync(Url(n, $"orders/{order}/issue-free"), new { orderVersion = 1 })).StatusCode);
        await Sql("UPDATE metro.orders SET created_at=clock_timestamp()-interval '40 days' WHERE id=@id", ("id", order));
        Assert.Equal(0, (await holder.Client.GetFromJsonAsync<JsonElement>(Url(n, "orders"))).GetProperty("total").GetInt32());
        Assert.Equal(1, (await holder.Client.GetFromJsonAsync<JsonElement>(Url(n, "orders") + $"&fromDate={Today.AddDays(-50):yyyy-MM-dd}")).GetProperty("total").GetInt32());
    }
    [PostgreSqlFact, Trait("Category", "PostgreSQL")]
    public async Task Paid_under_six_and_free_pass_are_rejected_without_tickets()
    {
        using var child = await AccountAsync(Today.AddYears(-5)); using var guardian = await AccountAsync();
        var n = await NetworkAsync(); var paid = await OrderAsync(n, child.Id); var payment = await PaymentAsync(n, paid);
        Assert.Equal("22023", (await Assert.ThrowsAsync<PostgresException>(() => Confirm(n, payment))).SqlState);
        var pass = await NetworkAsync("DAY"); var free = await OrderAsync(pass, child.Id, guardian.Id, true); await GuardianAsync(pass, guardian.Id, child.Id);
        Assert.Equal(HttpStatusCode.Conflict, (await guardian.Client.SendAsync(FreeRequest(pass, free, "no-child-pass"))).StatusCode);
        Assert.Equal(0L, await Sql("SELECT count(*) FROM metro.tickets WHERE order_id=@id", ("id", free)));
    }
    [PostgreSqlFact, Trait("Category", "PostgreSQL")]
    public async Task Free_confirmation_can_reference_future_paid_guardian_without_early_QR()
    {
        using var child = await AccountAsync(Today.AddYears(-5)); using var guardian = await AccountAsync(); var n = await NetworkAsync();
        var parent = await OrderAsync(n, guardian.Id, day: Today.AddDays(1)); await Confirm(n, await PaymentAsync(n, parent));
        await GuardianAsync(n, guardian.Id, child.Id); var order = await OrderAsync(n, child.Id, guardian.Id, true, Today.AddDays(1));
        var replies = await Task.WhenAll(guardian.Client.SendAsync(FreeRequest(n, order, "future-child-001")), guardian.Client.SendAsync(FreeRequest(n, order, "future-child-001")));
        Assert.All(replies, r => Assert.Equal(HttpStatusCode.Accepted, r.StatusCode)); await ProcessAsync();
        var id = await TicketId(order);
        Assert.Equal(await TicketId(parent), await Sql("SELECT related_ticket_id FROM metro.tickets WHERE id=@id", ("id", id)));
        Assert.Equal(0L, await Sql("SELECT count(*) FROM metro.ticket_credentials WHERE ticket_id=@id", ("id", id)));
        Assert.Equal(0L, await Sql("SELECT count(*) FROM metro.payments WHERE order_id=@id", ("id", order)));
    }
    [PostgreSqlFact, Trait("Category", "PostgreSQL")]
    public async Task QR_hash_mismatch_is_denied_and_missing_key_recovers_once_without_extension()
    {
        using var holder = await AccountAsync(); var n = await NetworkAsync("DAY"); var order = await OrderAsync(n, holder.Id); await Confirm(n, await PaymentAsync(n, order));
        var id = await TicketId(order); var end = await Sql("SELECT expires_at FROM metro.tickets WHERE id=@id", ("id", id));
        await ProcessAsync(new TicketQrCodec(Options.Create(new TicketQrOptions())));
        await Sql("UPDATE metro.ticket_issuance_jobs SET next_attempt_at=clock_timestamp() WHERE ticket_id=@id", ("id", id)); await ProcessAsync();
        Assert.Equal(end, await Sql("SELECT expires_at FROM metro.tickets WHERE id=@id", ("id", id)));
        Assert.Equal(HttpStatusCode.OK, (await holder.Client.GetAsync(Url(n, $"tickets/{id}/qr"))).StatusCode);
        await Sql("UPDATE metro.ticket_credentials SET token_hash=@hash WHERE ticket_id=@id", ("id", id), ("hash", new string('b', 64)));
        var response = await holder.Client.GetAsync(Url(n, $"tickets/{id}/qr")); Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.DoesNotContain(new string('b', 64), string.Join("\n", factory.TestLogs.Messages));
    }
    [PostgreSqlFact, Trait("Category", "PostgreSQL")]
    public async Task All_pass_products_use_paid_time_and_calendar_EndExclusive()
    {
        foreach (var product in new[] { "DAY", "WEEK", "MONTH" })
        {
            using var holder = await AccountAsync(); var n = await NetworkAsync(product); var order = await OrderAsync(n, holder.Id);
            await Confirm(n, await PaymentAsync(n, order));
            var paid = (DateTime)(await Sql("SELECT paid_at FROM metro.orders WHERE id=@id", ("id", order)))!;
            var expected = TicketRules.Validity(product, null, new DateTimeOffset(paid)); var id = await TicketId(order);
            Assert.Equal(expected.Start, await Sql("SELECT effective_from FROM metro.tickets WHERE id=@id", ("id", id)));
            Assert.Equal(expected.EndExclusive, await Sql("SELECT expires_at FROM metro.tickets WHERE id=@id", ("id", id)));
            await ProcessAsync(); Assert.Equal(HttpStatusCode.OK, (await holder.Client.GetAsync(Url(n, $"tickets/{id}/qr"))).StatusCode);
        }
    }
    [PostgreSqlFact, Trait("Category", "PostgreSQL")]
    public async Task Replayed_event_cannot_change_payload_and_used_or_expired_ticket_has_no_entry_QR()
    {
        using var holder = await AccountAsync(); var n = await NetworkAsync(); var order = await OrderAsync(n, holder.Id); var payment = await PaymentAsync(n, order);
        await Confirm(n, payment);
        Assert.Equal("23505", (await Assert.ThrowsAsync<PostgresException>(() => Confirm(n, payment, payloadHash: new string('c', 64)))).SqlState);
        await ProcessAsync(); var id = await TicketId(order);
        await Sql("UPDATE metro.tickets SET effective_from=clock_timestamp()-interval '2 days',expires_at=clock_timestamp()-interval '1 day' WHERE id=@id", ("id", id));
        Assert.Equal(HttpStatusCode.Conflict, (await holder.Client.GetAsync(Url(n, $"tickets/{id}/qr"))).StatusCode);
        await Sql("UPDATE metro.tickets SET status='USED' WHERE id=@id", ("id", id));
        Assert.Equal(HttpStatusCode.Conflict, (await holder.Client.GetAsync(Url(n, $"tickets/{id}/qr"))).StatusCode);
    }
    [PostgreSqlFact, Trait("Category", "PostgreSQL")]
    public async Task Corrected_child_birth_date_is_rechecked_before_creating_QR()
    {
        using var child = await AccountAsync(Today.AddYears(-5)); using var guardian = await AccountAsync(); var n = await NetworkAsync();
        var parent = await OrderAsync(n, guardian.Id); await Confirm(n, await PaymentAsync(n, parent)); await ProcessAsync();
        await GuardianAsync(n, guardian.Id, child.Id); var order = await OrderAsync(n, child.Id, guardian.Id, true);
        Assert.Equal(HttpStatusCode.Accepted, (await guardian.Client.SendAsync(FreeRequest(n, order, "corrected-birth-date"))).StatusCode);
        await Sql("UPDATE metro.user_profiles SET birth_date=@birth WHERE user_id=@user", ("birth", Today.AddYears(-6)), ("user", child.Id));
        await ProcessAsync(); var id = await TicketId(order);
        Assert.Equal(0L, await Sql("SELECT count(*) FROM metro.ticket_credentials WHERE ticket_id=@id", ("id", id)));
        Assert.Equal("FREE_PAIR_NOT_READY", await Sql("SELECT last_error_code FROM metro.ticket_issuance_jobs WHERE ticket_id=@id", ("id", id)));
        Assert.Equal(HttpStatusCode.Conflict, (await child.Client.GetAsync(Url(n, $"tickets/{id}/qr"))).StatusCode);
    }
    private sealed record Network(Guid Op, string Code, Guid Product, Guid Fare, Guid A, Guid B);
    private sealed record Account(string Id, HttpClient Client) : IDisposable { public void Dispose() => Client.Dispose(); }
}
