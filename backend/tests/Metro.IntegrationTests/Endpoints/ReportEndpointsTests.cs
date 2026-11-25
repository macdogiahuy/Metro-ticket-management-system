using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Metro.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using System.IdentityModel.Tokens.Jwt;
using Metro.Domain.Entities.Identity;
using Metro.Infrastructure.Persistence.Context;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace Metro.IntegrationTests.Endpoints;

[Collection(DatabaseIntegrationCollection.Name)]
public sealed class ReportEndpointsTests(MetroWebApplicationFactory factory)
{
    private const string Query = "?from=2026-11-01&to=2026-11-30";

    [PostgreSqlFact, Trait("Category", "PostgreSQL")]
    public async Task All_report_routes_require_authentication()
    {
        using var client = factory.CreateClient();
        foreach (var path in new[] { "/api/v1/reports/revenue", "/api/v1/reports/tickets", "/api/v1/reports/revenue/export" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path + Query)).StatusCode);
    }

    [PostgreSqlFact, Trait("Category", "PostgreSQL")]
    public async Task Authenticated_non_admin_is_forbidden()
    {
        using var client = await CreateUserAsync(role: null);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/reports/revenue" + Query)).StatusCode);
    }

    [PostgreSqlFact, Trait("Category", "PostgreSQL")]
    public async Task Admin_without_active_operator_membership_is_forbidden()
    {
        using var client = await CreateUserAsync("Admin");
        var response = await client.GetAsync("/api/v1/reports/revenue" + Query);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("REPORT_OPERATOR_FORBIDDEN", payload.RootElement.GetProperty("code").GetString());
    }

    [PostgreSqlFact, Trait("Category", "PostgreSQL")]
    public async Task Revenue_and_csv_match_frozen_order_and_are_isolated_by_operator()
    {
        using var client = await CreateUserAsync("Admin");
        var userId = new JwtSecurityTokenHandler().ReadJwtToken(client.DefaultRequestHeaders.Authorization!.Parameter).Subject;
        var op = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(PostgreSqlTestConfiguration.LoadRequired().ConnectionString);
        await connection.OpenAsync();
        await using (var insert = new NpgsqlCommand("""
            INSERT INTO metro.operators(id,code,name) VALUES(@op,@code,'Reports test');
            INSERT INTO metro.operator_accounts(operator_id,user_id,account_type,status,mfa_required) VALUES(@op,@user,'ADMIN','ACTIVE',true);
            """, connection))
        {
            insert.Parameters.AddWithValue("op", op);
            insert.Parameters.AddWithValue("code", $"RPT-{op:N}");
            insert.Parameters.AddWithValue("user", userId);
            await insert.ExecuteNonQueryAsync();
        }
        var product = Guid.NewGuid();
        var fare = Guid.NewGuid();
        var order = Guid.NewGuid();
        var line = Guid.NewGuid();
        var laterLine = Guid.NewGuid();
        await using (var seed = new NpgsqlCommand("""
            INSERT INTO metro.ticket_products(id,operator_id,code,name,product_type,expiry_rule,duration_value,duration_unit)
            VALUES(@product,@op,'DAY','Day','DAY','NEXT_LOCAL_MIDNIGHT',1,'DAY');
            INSERT INTO metro.fare_policy_versions(id,operator_id,code,version,scope_key,effective_from,created_by_user_id)
            VALUES(@fare,@op,'FARE',1,'TEST',now()-interval '1 day',@user);
            INSERT INTO metro.orders(id,operator_id,order_code,channel,payer_user_id,beneficiary_user_id,fare_policy_version_id,subtotal_minor,total_minor)
            VALUES(@order,@op,'ORDER','WEB',@user,@user,@fare,9000,9000);
            INSERT INTO metro.order_versions(operator_id,order_id,version,status,ticket_product_id,subtotal_minor,total_minor,fare_policy_version_id,confirmed_at)
            VALUES(@op,@order,1,'CONFIRMED',@product,9000,9000,@fare,now());
            INSERT INTO metro.payments(operator_id,order_id,payment_code,provider,method,amount_minor,status,idempotency_key,succeeded_at)
            VALUES(@op,@order,'PAY','TEST','CASH',12000,'SUCCEEDED','PAY',TIMESTAMPTZ '2026-11-01 17:00:00+00');
            UPDATE metro.orders SET subtotal_minor=7000,total_minor=7000 WHERE id=@order;
            INSERT INTO metro.lines(id,operator_id,code,name) VALUES(@line,@op,'L1','Line 1'),(@later,@op,'L2','Line 2');
            INSERT INTO metro.stations(id,operator_id,code,name) VALUES(@station1,@op,'S1','Origin'),(@station2,@op,'S2','Destination');
            INSERT INTO metro.trip_suggestions(operator_id,order_id,order_version,suggestion_code,beneficiary_user_id,service_date,origin_station_id,destination_station_id,line_id,status,no_trip_reason,source,computed_at,superseded_at)
            VALUES(@op,@order,1,'EARLY',@user,'2026-11-02',@station1,@station2,@line,'NO_TRIP','Fixture','MANUAL',TIMESTAMPTZ '2026-11-01 16:00:00+00',TIMESTAMPTZ '2026-11-01 18:00:00+00');
            INSERT INTO metro.trip_suggestions(operator_id,order_id,order_version,suggestion_code,beneficiary_user_id,service_date,origin_station_id,destination_station_id,line_id,status,no_trip_reason,source,computed_at)
            VALUES(@op,@order,1,'LATER',@user,'2026-11-02',@station1,@station2,@later,'NO_TRIP','Fixture','MANUAL',TIMESTAMPTZ '2026-11-01 18:00:00+00');
            INSERT INTO metro.order_items(id,operator_id,order_id,ticket_product_id,unit_price_minor,line_total_minor)
            VALUES(@item,@op,@order,@product,9000,9000);
            INSERT INTO metro.tickets(id,operator_id,ticket_code,order_item_id,ticket_product_id,sequence_no,fare_policy_version_id)
            VALUES(@ticket1,@op,'T1',@item,@product,1,@fare),(@ticket2,@op,'T2',@item,@product,2,@fare),(@ticket3,@op,'T3',@item,@product,3,@fare);
            INSERT INTO metro.refund_requests(id,operator_id,request_code,ticket_id,payment_id,reason_type,requested_amount_minor,status,created_at)
            SELECT @refund1,@op,'R1',@ticket1,id,'TEST',2000,'COMPLETED',TIMESTAMPTZ '2026-11-02 01:00:00+00' FROM metro.payments WHERE operator_id=@op AND order_id=@order;
            INSERT INTO metro.refund_requests(id,operator_id,request_code,ticket_id,payment_id,reason_type,requested_amount_minor,status,created_at)
            SELECT @refund2,@op,'R2',@ticket2,id,'TEST',1000,'SUBMITTED',TIMESTAMPTZ '2026-11-02 02:00:00+00' FROM metro.payments WHERE operator_id=@op AND order_id=@order;
            INSERT INTO metro.refund_transactions(operator_id,refund_request_id,payment_id,refund_code,provider,method,amount_minor,status,idempotency_key,succeeded_at)
            SELECT @op,@refund1,id,'REFUND','TEST','CASH',2000,'SUCCEEDED','REFUND',TIMESTAMPTZ '2026-11-02 01:00:00+00' FROM metro.payments WHERE operator_id=@op AND order_id=@order;
            INSERT INTO metro.refund_transactions(operator_id,refund_request_id,payment_id,refund_code,provider,method,amount_minor,status,idempotency_key)
            SELECT @op,@refund1,id,'FAILED','TEST','CASH',500,'FAILED','FAILED' FROM metro.payments WHERE operator_id=@op AND order_id=@order;
            INSERT INTO metro.orders(id,operator_id,order_code,channel,payer_user_id,beneficiary_user_id,fare_policy_version_id,subtotal_minor,total_minor)
            VALUES(@oldorder,@op,'OLD','WEB',@user,@user,@fare,9000,9000);
            INSERT INTO metro.order_versions(operator_id,order_id,version,status,ticket_product_id,subtotal_minor,total_minor,fare_policy_version_id,confirmed_at)
            VALUES(@op,@oldorder,1,'CONFIRMED',@product,9000,9000,@fare,now());
            INSERT INTO metro.payments(id,operator_id,order_id,payment_code,provider,method,amount_minor,status,idempotency_key,succeeded_at)
            VALUES(@oldpayment,@op,@oldorder,'OLD','TEST','CASH',9000,'SUCCEEDED','OLD',TIMESTAMPTZ '2026-10-30 10:00:00+00');
            INSERT INTO metro.order_items(id,operator_id,order_id,ticket_product_id,unit_price_minor,line_total_minor) VALUES(@olditem,@op,@oldorder,@product,9000,9000);
            INSERT INTO metro.tickets(id,operator_id,ticket_code,order_item_id,ticket_product_id,sequence_no,fare_policy_version_id)
            VALUES(@oldticket,@op,'OLD',@olditem,@product,1,@fare);
            INSERT INTO metro.refund_requests(id,operator_id,request_code,ticket_id,payment_id,reason_type,requested_amount_minor,status,created_at)
            VALUES(@oldrefund,@op,'OLD',@oldticket,@oldpayment,'TEST',3000,'COMPLETED',TIMESTAMPTZ '2026-11-02 03:00:00+00');
            INSERT INTO metro.refund_transactions(operator_id,refund_request_id,payment_id,refund_code,provider,method,amount_minor,status,idempotency_key,succeeded_at)
            VALUES(@op,@oldrefund,@oldpayment,'OLD','TEST','CASH',3000,'SUCCEEDED','OLD',TIMESTAMPTZ '2026-11-02 03:00:00+00');
            UPDATE metro.tickets SET issued_at = TIMESTAMPTZ '2026-11-02 03:00:00+00' WHERE operator_id = @op AND ticket_code IN('T1', 'T2', 'T3', 'OLD');
            INSERT INTO metro.ticket_issuance_jobs(ticket_id,operator_id,order_id,due_at, next_attempt_at, status) SELECT id, operator_id, order_id,TIMESTAMPTZ '2026-11-02 03:00:00+00',TIMESTAMPTZ '2026-11-02 03:00:00+00',
                CASE ticket_code
                    WHEN 'T1' THEN 'READY'
                    WHEN 'T2' THEN 'PENDING'
                    WHEN 'T3' THEN 'NEEDS_SUPPORT'
                    WHEN 'OLD' THEN 'CANCELLED'
                END FROM metro.tickets WHERE operator_id = @op AND ticket_code IN ('T1', 'T2', 'T3', 'OLD');
            """, connection))
        {
            seed.Parameters.AddWithValue("op", op); seed.Parameters.AddWithValue("user", userId);
            seed.Parameters.AddWithValue("product", product); seed.Parameters.AddWithValue("fare", fare); seed.Parameters.AddWithValue("order", order);
            seed.Parameters.AddWithValue("line", line); seed.Parameters.AddWithValue("later", laterLine);
            foreach (var name in new[] { "station1", "station2", "item", "ticket1", "ticket2", "ticket3", "refund1", "refund2", "oldorder", "oldpayment", "olditem", "oldticket", "oldrefund" }) seed.Parameters.AddWithValue(name, Guid.NewGuid());
            await seed.ExecuteNonQueryAsync();
        }
        using (var ticketsResponse = await client.GetAsync("/api/v1/reports/tickets" + Query))
        {
            Assert.True(ticketsResponse.StatusCode == HttpStatusCode.OK, await ticketsResponse.Content.ReadAsStringAsync());
            using var tickets = JsonDocument.Parse(await ticketsResponse.Content.ReadAsStringAsync());
            var totals = tickets.RootElement.GetProperty("totals");
            Assert.Equal(1, totals.GetProperty("readyTicketCount").GetInt64());
            Assert.Equal(2, totals.GetProperty("pendingIssuanceTicketCount").GetInt64());
            Assert.Equal(4, tickets.RootElement.GetProperty("totalRows").GetInt64());
        }
        foreach (var status in new[] { "READY", "PENDING", "NEEDS_SUPPORT", "CANCELLED" })
        {
            using var response = await client.GetAsync("/api/v1/reports/tickets" + Query + "&issuanceStatus=" + status);
            Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(1, json.RootElement.GetProperty("totalRows").GetInt64());
            Assert.Equal(status, json.RootElement.GetProperty("rows")[0].GetProperty("issuanceStatus").GetString());
        }
        using (var invalidStatus = await client.GetAsync("/api/v1/reports/tickets" + Query + "&issuanceStatus=INVALID"))
            Assert.Equal(HttpStatusCode.BadRequest, invalidStatus.StatusCode);
        foreach (var path in new[] { "/api/v1/reports/revenue", "/api/v1/reports/tickets" })
        {
            using var response = await client.GetAsync(path + Query);
            Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            if (path.EndsWith("revenue", StringComparison.Ordinal))
            {
                using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var totals = payload.RootElement.GetProperty("totals");
                Assert.Equal(12000, totals.GetProperty("actualCollectedMinor").GetInt64());
                Assert.Equal(9000, totals.GetProperty("validPaidOrderValueMinor").GetInt64());
                Assert.Equal(3000, totals.GetProperty("incorrectOrExcessCollectedMinor").GetInt64());
                Assert.Equal(5000, totals.GetProperty("successfulRefundMinor").GetInt64());
                Assert.Equal(1000, totals.GetProperty("pendingRefundMinor").GetInt64());
                Assert.Equal(7000, totals.GetProperty("netCashflowMinor").GetInt64());
                Assert.Equal("2026-11-02", payload.RootElement.GetProperty("rows")[0].GetProperty("businessDate").GetString());
            }
        }
        using var csv = await client.GetAsync("/api/v1/reports/revenue/export" + Query + "&purpose=Test+report");
        Assert.True(csv.StatusCode == HttpStatusCode.OK, await csv.Content.ReadAsStringAsync());
        Assert.Contains("businessDate,paymentCount", await csv.Content.ReadAsStringAsync());
        Assert.Contains("2026-11-02,1,12000,9000,3000,5000,1000,7000", await csv.Content.ReadAsStringAsync());
        using var byLine = await client.GetAsync("/api/v1/reports/revenue" + Query + "&lineId=" + line + "&ticketProductId=" + product);
        Assert.Equal(HttpStatusCode.OK, byLine.StatusCode);
        using (var filtered = JsonDocument.Parse(await byLine.Content.ReadAsStringAsync()))
        {
            Assert.Equal(12000, filtered.RootElement.GetProperty("totals").GetProperty("actualCollectedMinor").GetInt64());
            Assert.Equal(2000, filtered.RootElement.GetProperty("totals").GetProperty("successfulRefundMinor").GetInt64());
        }
        using var filteredCsv = await client.GetAsync("/api/v1/reports/revenue/export" + Query + "&purpose=Line+test&lineId=" + line + "&ticketProductId=" + product);
        Assert.Equal(HttpStatusCode.OK, filteredCsv.StatusCode);
        Assert.Contains("2026-11-02,1,12000,9000,3000,2000,1000,10000", await filteredCsv.Content.ReadAsStringAsync());
        using var byLaterLine = await client.GetAsync("/api/v1/reports/revenue" + Query + "&lineId=" + laterLine);
        Assert.Equal(HttpStatusCode.OK, byLaterLine.StatusCode);
        using (var filtered = JsonDocument.Parse(await byLaterLine.Content.ReadAsStringAsync()))
            Assert.Equal(0, filtered.RootElement.GetProperty("totals").GetProperty("actualCollectedMinor").GetInt64());
        using var noProduct = await client.GetAsync("/api/v1/reports/revenue" + Query + "&ticketProductId=" + Guid.NewGuid());
        Assert.Equal(HttpStatusCode.OK, noProduct.StatusCode);
        using (var filtered = JsonDocument.Parse(await noProduct.Content.ReadAsStringAsync()))
            Assert.Equal(0, filtered.RootElement.GetProperty("totals").GetProperty("actualCollectedMinor").GetInt64());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/reports/revenue?from=2026-11-01&to=2026-12-02")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/reports/revenue/export" + Query)).StatusCode);
        await using var audit = new NpgsqlCommand("SELECT count(*) FROM metro.audit_logs WHERE operator_id=@op AND action='REPORT_EXPORT' AND actor_user_id=@user", connection);
        audit.Parameters.AddWithValue("op", op);
        audit.Parameters.AddWithValue("user", userId);
        Assert.Equal(2L, await audit.ExecuteScalarAsync());
        using var other = await CreateUserAsync("Admin");
        var otherId = new JwtSecurityTokenHandler().ReadJwtToken(other.DefaultRequestHeaders.Authorization!.Parameter).Subject;
        var otherOp = Guid.NewGuid();
        await using (var second = new NpgsqlCommand("""
            INSERT INTO metro.operators(id,code,name) VALUES(@op,@code,'Other reports test');
            INSERT INTO metro.operator_accounts(operator_id,user_id,account_type,status,mfa_required) VALUES(@op,@user,'ADMIN','ACTIVE',true);
            """, connection))
        {
            second.Parameters.AddWithValue("op", otherOp); second.Parameters.AddWithValue("code", $"RPT-{otherOp:N}");
            second.Parameters.AddWithValue("user", otherId); await second.ExecuteNonQueryAsync();
        }
        using var otherRevenue = await other.GetAsync("/api/v1/reports/revenue" + Query);
        Assert.True(otherRevenue.StatusCode == HttpStatusCode.OK, await otherRevenue.Content.ReadAsStringAsync());
        using var otherPayload = JsonDocument.Parse(await otherRevenue.Content.ReadAsStringAsync());
        Assert.Equal(0, otherPayload.RootElement.GetProperty("totals").GetProperty("actualCollectedMinor").GetInt64());
    }

    [PostgreSqlFact, Trait("Category", "PostgreSQL")]
    public async Task Revenue_uses_confirmed_status_and_vietnamese_calendar_boundaries()
    {
        using var client = await CreateUserAsync("Admin");
        var user = new JwtSecurityTokenHandler().ReadJwtToken(client.DefaultRequestHeaders.Authorization!.Parameter).Subject;
        var op = Guid.NewGuid(); var product = Guid.NewGuid(); var otherProduct = Guid.NewGuid(); var fare = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(PostgreSqlTestConfiguration.LoadRequired().ConnectionString);
        await connection.OpenAsync();
        await using (var seed = new NpgsqlCommand("""
            INSERT INTO metro.operators(id,code,name) VALUES(@op,@code,'Revenue statuses');
            INSERT INTO metro.operator_accounts(operator_id,user_id,account_type,status,mfa_required) VALUES(@op,@user,'ADMIN','ACTIVE',true);
            INSERT INTO metro.ticket_products(id,operator_id,code,name,product_type,expiry_rule,duration_value,duration_unit) VALUES(@product,@op,'DAY','Day','DAY','NEXT_LOCAL_MIDNIGHT',1,'DAY');
            INSERT INTO metro.ticket_products(id,operator_id,code,name,product_type,expiry_rule,duration_value,duration_unit) VALUES(@otherproduct,@op,'WEEK','Week','WEEK','NEXT_LOCAL_MIDNIGHT',7,'DAY');
            INSERT INTO metro.fare_policy_versions(id,operator_id,code,version,scope_key,effective_from,created_by_user_id) VALUES(@fare,@op,'FARE',1,'TEST',now()-interval '1 day',@user);
            CREATE TEMP TABLE report_status_fixture(n integer, order_id uuid);
            INSERT INTO report_status_fixture SELECT n,gen_random_uuid() FROM generate_series(1,9) n;
            INSERT INTO metro.orders(id,operator_id,order_code,channel,payer_user_id,beneficiary_user_id,fare_policy_version_id,subtotal_minor,total_minor)
            SELECT order_id,@op,'ORDER-'||n,'WEB',@user,@user,@fare,100,100 FROM report_status_fixture;
            INSERT INTO metro.order_versions(operator_id,order_id,version,status,ticket_product_id,subtotal_minor,total_minor,fare_policy_version_id,confirmed_at)
            SELECT @op,order_id,1,'CONFIRMED',CASE WHEN n=3 THEN @otherproduct ELSE @product END,100,100,@fare,now() FROM report_status_fixture;
            INSERT INTO metro.payments(operator_id,order_id,payment_code,provider,method,amount_minor,status,idempotency_key,succeeded_at,reconciliation_status)
            SELECT @op,order_id,'PAY-'||n,'TEST','CASH',100,
                (ARRAY['SUCCEEDED','PARTIALLY_REFUNDED','REFUNDED','SUCCEEDED','PENDING','FAILED','EXPIRED','CANCELLED','REVIEW_REQUIRED'])[n],
                'PAY-'||n, CASE n WHEN 1 THEN TIMESTAMPTZ '2026-11-01 16:59:59+00' WHEN 3 THEN TIMESTAMPTZ '2026-11-02 16:59:59+00' WHEN 4 THEN TIMESTAMPTZ '2026-11-02 17:00:00+00' ELSE TIMESTAMPTZ '2026-11-01 17:00:00+00' END,
                'UNRECONCILED' FROM report_status_fixture;
            """, connection))
        {
            seed.Parameters.AddWithValue("op", op); seed.Parameters.AddWithValue("code", $"RPT-{op:N}"); seed.Parameters.AddWithValue("user", user);
            seed.Parameters.AddWithValue("product", product); seed.Parameters.AddWithValue("otherproduct", otherProduct); seed.Parameters.AddWithValue("fare", fare);
            await seed.ExecuteNonQueryAsync();
        }
        foreach (var productId in new[] { product, otherProduct })
        {
            using var response = await client.GetAsync($"/api/v1/reports/revenue?from=2026-11-02&to=2026-11-02&ticketProductId={productId}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(100, json.RootElement.GetProperty("totals").GetProperty("actualCollectedMinor").GetInt64());
        }
        foreach (var (day, expected) in new[] { ("2026-11-01", 100L), ("2026-11-02", 200L), ("2026-11-03", 100L) })
        {
            using var response = await client.GetAsync($"/api/v1/reports/revenue?from={day}&to={day}");
            Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(expected, json.RootElement.GetProperty("totals").GetProperty("actualCollectedMinor").GetInt64());
        }
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/reports/revenue?from=2026-11-01&to=2026-12-01")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/reports/revenue?from=2026-11-01&to=2026-12-02")).StatusCode);
        using var export = await client.GetAsync("/api/v1/reports/revenue/export?from=2026-11-01&to=2026-12-01&purpose=Boundary+test");
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        var csvRows = (await export.Content.ReadAsStringAsync()).Trim().Split('\n');
        Assert.Equal(4, csvRows.Length); // header plus three nonempty business days
        Assert.Contains(csvRows, row => row.StartsWith("2026-11-01,", StringComparison.Ordinal));
        Assert.Contains(csvRows, row => row.StartsWith("2026-11-02,", StringComparison.Ordinal));
        Assert.Contains(csvRows, row => row.StartsWith("2026-11-03,", StringComparison.Ordinal));
        using var tooManyDays = await client.GetAsync("/api/v1/reports/revenue/export?from=2026-11-01&to=2026-12-02&purpose=Boundary+test");
        Assert.Equal(HttpStatusCode.BadRequest, tooManyDays.StatusCode);
    }

    [PostgreSqlFact, Trait("Category", "PostgreSQL")]
    public async Task Export_rolls_back_report_job_when_audit_save_fails()
    {
        using var authenticated = await CreateUserAsync("Admin");
        var user = new JwtSecurityTokenHandler().ReadJwtToken(authenticated.DefaultRequestHeaders.Authorization!.Parameter).Subject;
        var op = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(PostgreSqlTestConfiguration.LoadRequired().ConnectionString);
        await connection.OpenAsync();
        await using (var seed = new NpgsqlCommand("""
            INSERT INTO metro.operators(id,code,name) VALUES(@op,@code,'Audit failure test');
            INSERT INTO metro.operator_accounts(operator_id,user_id,account_type,status,mfa_required)
            VALUES(@op,@user,'ADMIN','ACTIVE',true);
            """, connection))
        {
            seed.Parameters.AddWithValue("op", op);
            seed.Parameters.AddWithValue("code", $"RPT-{op:N}");
            seed.Parameters.AddWithValue("user", user);
            await seed.ExecuteNonQueryAsync();
        }

        var interceptor = new FailReportAuditSave();
        using var failingApp = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddDbContext<ApplicationDbContext>(options => options.AddInterceptors(interceptor))));
        using var client = failingApp.CreateClient();
        client.DefaultRequestHeaders.Authorization = authenticated.DefaultRequestHeaders.Authorization;
        using var response = await client.GetAsync("/api/v1/reports/revenue/export" + Query + "&purpose=Audit+failure+test");
        Assert.True(interceptor.Attempted, "The injected audit failure must actually run.");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.NotEqual("text/csv", response.Content.Headers.ContentType?.MediaType);

        await using var persisted = new NpgsqlCommand("""
            SELECT (SELECT count(*) FROM metro.report_jobs WHERE operator_id=@op),
                   (SELECT count(*) FROM metro.audit_logs WHERE operator_id=@op AND action='REPORT_EXPORT');
            """, connection);
        persisted.Parameters.AddWithValue("op", op);
        await using var reader = await persisted.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(0L, reader.GetInt64(0));
        Assert.Equal(0L, reader.GetInt64(1));
    }

    private sealed class FailReportAuditSave : SaveChangesInterceptor
    {
        public bool Attempted { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<AuditLog>().Any(entry =>
                    entry.State == EntityState.Added && entry.Entity.Action == "REPORT_EXPORT") == true)
            {
                Attempted = true;
                throw new DbUpdateException("Simulated report audit write failure.");
            }
            return ValueTask.FromResult(result);
        }
    }

    private async Task<HttpClient> CreateUserAsync(string? role)
    {
        var email = $"report-{Guid.NewGuid():N}@example.invalid";
        var password = $"Metro!{Guid.NewGuid():N}Aa1";
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var user = new IdentityUser { Id = Guid.NewGuid().ToString("N"), UserName = email, Email = email, EmailConfirmed = true };
        Assert.True((await users.CreateAsync(user, password)).Succeeded);
        if (role is not null)
        {
            if (!await roles.RoleExistsAsync(role)) Assert.True((await roles.CreateAsync(new IdentityRole(role))).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
        }
        var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", payload.RootElement.GetProperty("accessToken").GetString());
        return client;
    }


}
