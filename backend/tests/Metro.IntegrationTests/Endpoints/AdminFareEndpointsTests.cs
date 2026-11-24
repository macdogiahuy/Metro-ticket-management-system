using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Metro.Application.Abstractions.MultiTenancy;
using Metro.Domain.Entities.Identity;
using Metro.Domain.Entities.Orders;
using Metro.Infrastructure.Persistence.Context;
using Metro.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Metro.IntegrationTests.Endpoints;

[Collection(DatabaseIntegrationCollection.Name)]
public sealed class AdminFareEndpointsTests(MetroWebApplicationFactory factory)
{
    [PostgreSqlFact]
    [Trait("Category", "PostgreSQL")]
    public async Task Admin_fares_enforce_auth_null_safe_key_maker_checker_overlap_idempotency_and_audit()
    {
        var seed = await SeedAsync();
        using var client = factory.CreateClient();

        // 1. Auth checks: 401 unauth, 403 Passenger.
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/admin/fares")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await LoginAsync(client, seed.Passenger));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/admin/fares")).StatusCode);

        var admin1Token = await LoginAsync(client, seed.Admin1);
        var admin2Token = await LoginAsync(client, seed.Admin2);
        var now = DateTime.UtcNow;

        // 2. Admin 1 creates DRAFT fare version v1 with Idempotency-Key (replay + conflict verified).
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin1Token);
        var v1Body = new { code = $"FV1-{seed.Suffix}", scopeKey = seed.ScopeKey, effectiveFrom = now.AddMinutes(-5), effectiveTo = now.AddDays(30), reason = "Initial v1" };
        var v1Res = await SendWithIdemAsync(client, HttpMethod.Post, "/api/v1/admin/fares", v1Body, $"fv1-{seed.Suffix}");
        Assert.Equal(HttpStatusCode.Created, v1Res.StatusCode);
        using var v1Doc = JsonDocument.Parse(await v1Res.Content.ReadAsStringAsync());
        var v1Id = v1Doc.RootElement.GetProperty("id").GetGuid();
        Assert.Equal(1, v1Doc.RootElement.GetProperty("version").GetInt32());

        var v1Replay = await SendWithIdemAsync(client, HttpMethod.Post, "/api/v1/admin/fares", v1Body, $"fv1-{seed.Suffix}");
        Assert.Equal(HttpStatusCode.Created, v1Replay.StatusCode);
        Assert.Equal("true", v1Replay.Headers.GetValues("X-Idempotency-Replayed").Single());

        await AssertProblemAsync(
            await SendWithIdemAsync(client, HttpMethod.Post, "/api/v1/admin/fares",
                new { code = $"FV1-DIFF-{seed.Suffix}", scopeKey = seed.ScopeKey, effectiveFrom = now, effectiveTo = now.AddDays(10), reason = "diff" }, $"fv1-{seed.Suffix}"),
            HttpStatusCode.Conflict, "IDEMPOTENCY_KEY_CONFLICT");

        // 3. Add NULL-safe matrix entry for DAY pass (30,000 VND) and verify duplicate NULL-scope is blocked with 409!
        var entryBody = new
        {
            ticketProductId = seed.DayProductId, lineId = (Guid?)null, originStationId = (Guid?)null,
            destinationStationId = (Guid?)null, paymentChannel = "ANY", eligibilityGroup = "ORDINARY",
            amountMinor = 30_000L, sellable = true, reason = "Base day fare"
        };
        var entryRes = await SendWithIdemAsync(client, HttpMethod.Post, $"/api/v1/admin/fares/{v1Id}/entries", entryBody, $"e1-{seed.Suffix}");
        Assert.Equal(HttpStatusCode.Created, entryRes.StatusCode);

        await AssertProblemAsync(
            await client.PostAsJsonAsync($"/api/v1/admin/fares/{v1Id}/entries", entryBody with { amountMinor = 35_000L }),
            HttpStatusCode.Conflict, "FARE_MATRIX_DUPLICATE");

        // 4. Maker-checker: Admin 1 cannot approve own version (409).
        await AssertProblemAsync(
            await client.PostAsJsonAsync($"/api/v1/admin/fares/{v1Id}/approve", new { reason = "Self approve" }),
            HttpStatusCode.Conflict, "MAKER_CHECKER_VIOLATION");

        // 5. Admin 2 approves & publishes v1; immutability blocks adding entries afterwards (409).
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin2Token);
        var approvedV1 = await SendWithIdemAsync(client, HttpMethod.Post, $"/api/v1/admin/fares/{v1Id}/approve", new { reason = "Reviewed OK" }, $"app1-{seed.Suffix}");
        Assert.Equal(HttpStatusCode.OK, approvedV1.StatusCode);

        await AssertProblemAsync(
            await client.PostAsJsonAsync($"/api/v1/admin/fares/{v1Id}/entries", entryBody with { paymentChannel = "NON_CASH" }),
            HttpStatusCode.Conflict, "FARE_VERSION_IMMUTABLE");

        var publishedV1 = await SendWithIdemAsync(client, HttpMethod.Post, $"/api/v1/admin/fares/{v1Id}/publish", new { reason = "Go live" }, $"pub1-{seed.Suffix}");
        Assert.Equal(HttpStatusCode.OK, publishedV1.StatusCode);

        // Public UC09 quote immediately uses newly published v1 (30,000 VND).
        using var publicClient = factory.CreateClient();
        var quoteRes = await publicClient.GetAsync($"/api/v1/fares/quote?productCode={seed.DayCode}&operatorCode={seed.OperatorCode}");
        Assert.Equal(HttpStatusCode.OK, quoteRes.StatusCode);
        using (var qDoc = JsonDocument.Parse(await quoteRes.Content.ReadAsStringAsync()))
        {
            Assert.Equal(v1Id, qDoc.RootElement.GetProperty("farePolicy").GetProperty("id").GetGuid());
            Assert.Equal(30_000, qDoc.RootElement.GetProperty("totalAmountMinor").GetInt64());
        }

        // 6. Overlapping published effective window blocked by PostgreSQL GiST exclusion -> 409 FARE_VERSION_WINDOW_OVERLAP.
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin1Token);
        var v2Res = await client.PostAsJsonAsync("/api/v1/admin/fares",
            new { code = $"FV2-{seed.Suffix}", scopeKey = seed.ScopeKey, effectiveFrom = now.AddDays(5), effectiveTo = now.AddDays(40), reason = "Overlapping draft" });
        Assert.Equal(HttpStatusCode.Created, v2Res.StatusCode);
        using var v2Doc = JsonDocument.Parse(await v2Res.Content.ReadAsStringAsync());
        var v2Id = v2Doc.RootElement.GetProperty("id").GetGuid();
        Assert.Equal(2, v2Doc.RootElement.GetProperty("version").GetInt32());
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync($"/api/v1/admin/fares/{v2Id}/entries", entryBody with { amountMinor = 40_000L })).StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin2Token);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/v1/admin/fares/{v2Id}/approve", new { reason = "OK" })).StatusCode);
        await AssertProblemAsync(
            await client.PostAsJsonAsync($"/api/v1/admin/fares/{v2Id}/publish", new { reason = "Overlap attempt" }),
            HttpStatusCode.Conflict, "FARE_VERSION_WINDOW_OVERLAP");

        // 7. Verify audit logs persisted for v1.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<IOperatorContext>().CurrentOperatorId = seed.OperatorId;
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var fareAudits = await db.AuditLogs.AsNoTracking()
                .Where(a => a.EntityType == "FARE_POLICY_VERSION" && a.EntityId == v1Id.ToString()).ToListAsync();
            Assert.Equal(3, fareAudits.Count); // CREATE, APPROVE, PUBLISH
        }

        // 8. No PII or credentials logged.
        var logs = string.Join(Environment.NewLine, factory.TestLogs.Messages);
        Assert.DoesNotContain(seed.Admin1.Email, logs, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(seed.Admin2.Password, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(admin1Token, logs, StringComparison.Ordinal);
    }

    private static async Task<HttpResponseMessage> SendWithIdemAsync(HttpClient client, HttpMethod method, string url, object body, string key)
    {
        using var req = new HttpRequestMessage(method, url) { Content = JsonContent.Create(body) };
        req.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(req);
    }

    private static async Task<string> LoginAsync(HttpClient client, ActorCreds creds)
    {
        var res = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = creds.Email, password = creds.Password });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("accessToken").GetString()!;
    }

    private async Task<SeededFareAdmin> SeedAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        var opId = Guid.NewGuid();
        var opCode = $"AF-{suffix}";
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IOperatorContext>().CurrentOperatorId = opId;
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        db.Operators.Add(new Operator { Id = opId, Code = opCode, Name = $"Admin Fare Op {suffix}", Status = "ACTIVE" });
        await db.SaveChangesAsync();

        var passenger = await CreateUserAsync(users, roles, db, opId, "Passenger", "PASSENGER", false, $"afp-{suffix}");
        var admin1 = await CreateUserAsync(users, roles, db, opId, "Admin", "ADMIN", true, $"afa1-{suffix}");
        var admin2 = await CreateUserAsync(users, roles, db, opId, "Admin", "ADMIN", true, $"afa2-{suffix}");
        var dayProduct = new TicketProduct
        {
            OperatorId = opId, Code = $"DAY-{suffix}", Name = "Day pass", ProductType = "DAY",
            DurationValue = 1, DurationUnit = "DAY", ExpiryRule = "NEXT_LOCAL_MIDNIGHT", CreatedByUserId = admin1.Id
        };
        db.TicketProducts.Add(dayProduct);
        await db.SaveChangesAsync();
        return new SeededFareAdmin(opId, opCode, suffix, $"SCOPE-{suffix}", dayProduct.Id, dayProduct.Code, passenger, admin1, admin2);
    }

    private static async Task<ActorCreds> CreateUserAsync(
        UserManager<IdentityUser> users, RoleManager<IdentityRole> roles, ApplicationDbContext db,
        Guid opId, string roleName, string accountType, bool mfaRequired, string prefix)
    {
        var id = Guid.NewGuid().ToString("N");
        var email = $"{prefix}@example.invalid";
        var pw = $"Metro!{Guid.NewGuid():N}Aa1";
        var user = new IdentityUser { Id = id, UserName = email, Email = email, EmailConfirmed = true };
        Assert.True((await users.CreateAsync(user, pw)).Succeeded);
        if (!await roles.RoleExistsAsync(roleName))
            Assert.True((await roles.CreateAsync(new IdentityRole(roleName))).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, roleName)).Succeeded);
        db.OperatorAccounts.Add(new OperatorAccount { OperatorId = opId, UserId = id, AccountType = accountType, Status = "ACTIVE", MfaRequired = mfaRequired });
        return new ActorCreds(id, email, pw);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, problem.RootElement.GetProperty("code").GetString());
    }

    private sealed record ActorCreds(string Id, string Email, string Password);
    private sealed record SeededFareAdmin(
        Guid OperatorId, string OperatorCode, string Suffix, string ScopeKey,
        Guid DayProductId, string DayCode, ActorCreds Passenger, ActorCreds Admin1, ActorCreds Admin2);
}
