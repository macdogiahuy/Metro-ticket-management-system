using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Metro.Application.Abstractions.MultiTenancy;
using Metro.Domain.Entities.Identity;
using Metro.Infrastructure.Persistence.Context;
using Metro.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Metro.IntegrationTests.Endpoints;

[Collection(DatabaseIntegrationCollection.Name)]
public sealed class AdminProductEndpointsTests(MetroWebApplicationFactory factory)
{
    [PostgreSqlFact]
    [Trait("Category", "PostgreSQL")]
    public async Task Admin_products_enforce_401_403_duration_validation_idempotency_and_audit()
    {
        var seed = await SeedActorsAsync();
        using var client = factory.CreateClient();

        // 1. Unauthenticated -> 401.
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/admin/products")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/admin/products", ValidDayBody($"U-{seed.Suffix}"))).StatusCode);

        // 2. Passenger & StationStaff -> 403 Forbidden.
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await LoginAsync(client, seed.Passenger));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/admin/products")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/v1/admin/products", ValidDayBody($"P-{seed.Suffix}"))).StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await LoginAsync(client, seed.Staff));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/admin/products")).StatusCode);

        // 3. Admin -> validation guards (missing duration, 3-day ticket, capacityControlled).
        var adminToken = await LoginAsync(client, seed.Admin);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        await AssertProblemAsync(await client.PostAsJsonAsync("/api/v1/admin/products", new
        {
            code = $"NODUR-{seed.Suffix}", name = "Missing duration", productType = "MONTH",
            durationValue = (int?)null, durationUnit = "MONTH", expiryRule = "ELAPSED_DURATION",
            requiresNamedHolder = false, capacityControlled = false, status = "ACTIVE", reason = "test"
        }), HttpStatusCode.BadRequest, "PRODUCT_DURATION_REQUIRED");

        await AssertProblemAsync(await client.PostAsJsonAsync("/api/v1/admin/products", new
        {
            code = $"DAY3-{seed.Suffix}", name = "3-day prohibited", productType = "DAY",
            durationValue = 3, durationUnit = "DAY", expiryRule = "NEXT_LOCAL_MIDNIGHT",
            requiresNamedHolder = false, capacityControlled = false, status = "ACTIVE", reason = "test"
        }), HttpStatusCode.BadRequest, "UNSUPPORTED_PRODUCT_DURATION");

        await AssertProblemAsync(await client.PostAsJsonAsync("/api/v1/admin/products", new
        {
            code = $"CAP-{seed.Suffix}", name = "Legacy capacity", productType = "SINGLE_TRIP",
            durationValue = (int?)null, durationUnit = (string?)null, expiryRule = "SINGLE_JOURNEY",
            requiresNamedHolder = false, capacityControlled = true, status = "ACTIVE", reason = "test"
        }), HttpStatusCode.BadRequest, "CAPACITY_CONTROLLED_NOT_SUPPORTED");

        // 4. Create valid DAY pass with Idempotency-Key -> 201 Created, replay returns exact 201 + header, conflict returns 409.
        var idemKey = $"prod-create-{seed.Suffix}";
        var dayBody = ValidDayBody($"DAY-{seed.Suffix}");
        var first = await SendWithIdemAsync(client, HttpMethod.Post, "/api/v1/admin/products", dayBody, idemKey);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        using var firstDoc = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        var productId = firstDoc.RootElement.GetProperty("id").GetGuid();

        var replay = await SendWithIdemAsync(client, HttpMethod.Post, "/api/v1/admin/products", dayBody, idemKey);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal("true", replay.Headers.GetValues("X-Idempotency-Replayed").Single());
        using (var replayDoc = JsonDocument.Parse(await replay.Content.ReadAsStringAsync()))
            Assert.Equal(productId, replayDoc.RootElement.GetProperty("id").GetGuid());

        var conflict = await SendWithIdemAsync(client, HttpMethod.Post, "/api/v1/admin/products", ValidDayBody($"OTHER-{seed.Suffix}"), idemKey);
        await AssertProblemAsync(conflict, HttpStatusCode.Conflict, "IDEMPOTENCY_KEY_CONFLICT");

        // Duplicate code without Idempotency-Key -> 409 PRODUCT_CODE_EXISTS.
        await AssertProblemAsync(await client.PostAsJsonAsync("/api/v1/admin/products", dayBody),
            HttpStatusCode.Conflict, "PRODUCT_CODE_EXISTS");

        // 5. Update product status (suspend without deleting) -> 200 OK & verify audit logs.
        var updateRes = await SendWithIdemAsync(client, HttpMethod.Put, $"/api/v1/admin/products/{productId}", new
        {
            name = "Ve ngay cap nhat", durationValue = 1, durationUnit = "DAY",
            expiryRule = "NEXT_LOCAL_MIDNIGHT", requiresNamedHolder = false,
            status = "SUSPENDED", reason = "Temporary suspension"
        }, $"prod-upd-{seed.Suffix}");
        Assert.Equal(HttpStatusCode.OK, updateRes.StatusCode);
        using (var updDoc = JsonDocument.Parse(await updateRes.Content.ReadAsStringAsync()))
            Assert.Equal("SUSPENDED", updDoc.RootElement.GetProperty("status").GetString());

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<IOperatorContext>().CurrentOperatorId = seed.OperatorId;
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var audits = await db.AuditLogs.AsNoTracking()
                .Where(a => a.EntityType == "TICKET_PRODUCT" && a.EntityId == productId.ToString())
                .OrderBy(a => a.OccurredAt).ToListAsync();
            Assert.Equal(2, audits.Count);
            Assert.Equal("CREATE_TICKET_PRODUCT", audits[0].Action);
            Assert.Null(audits[0].BeforeHash);
            Assert.NotNull(audits[0].AfterHash);
            Assert.Equal("UPDATE_TICKET_PRODUCT", audits[1].Action);
            Assert.NotNull(audits[1].BeforeHash);
            Assert.NotNull(audits[1].AfterHash);
            Assert.Contains("status", audits[1].ChangedFields);
        }

        // 6. No PII or credentials in logs.
        var logs = string.Join(Environment.NewLine, factory.TestLogs.Messages);
        Assert.DoesNotContain(seed.Admin.Email, logs, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(seed.Admin.Password, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(adminToken, logs, StringComparison.Ordinal);
    }

    private static object ValidDayBody(string code) => new
    {
        code, name = "Ve ngay", productType = "DAY", durationValue = 1, durationUnit = "DAY",
        expiryRule = "NEXT_LOCAL_MIDNIGHT", requiresNamedHolder = false, capacityControlled = false,
        status = "ACTIVE", reason = "Initial product setup"
    };

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

    private async Task<SeededActors> SeedActorsAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        var opId = Guid.NewGuid();
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IOperatorContext>().CurrentOperatorId = opId;
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        db.Operators.Add(new Operator { Id = opId, Code = $"AP-{suffix}", Name = $"Admin Prod Op {suffix}", Status = "ACTIVE" });
        await db.SaveChangesAsync();

        var passenger = await CreateUserAsync(users, roles, db, opId, "Passenger", "PASSENGER", false, $"p-{suffix}");
        var staff = await CreateUserAsync(users, roles, db, opId, "StationStaff", "STAFF", true, $"s-{suffix}");
        var admin = await CreateUserAsync(users, roles, db, opId, "Admin", "ADMIN", true, $"a-{suffix}");
        await db.SaveChangesAsync();
        return new SeededActors(opId, suffix, passenger, staff, admin);
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
    private sealed record SeededActors(Guid OperatorId, string Suffix, ActorCreds Passenger, ActorCreds Staff, ActorCreds Admin);
}
