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
public sealed class AdminEligibilityPolicyEndpointsTests(MetroWebApplicationFactory factory)
{
    [PostgreSqlFact]
    [Trait("Category", "PostgreSQL")]
    public async Task Admin_eligibility_policies_enforce_auth_srs_groups_maker_checker_idempotency_and_audit()
    {
        var seed = await SeedAsync();
        using var client = factory.CreateClient();

        // 1. 401 unauthenticated, 403 non-Admin.
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/admin/eligibility-policies")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await LoginAsync(client, seed.Passenger));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/admin/eligibility-policies")).StatusCode);

        var admin1Token = await LoginAsync(client, seed.Admin1);
        var admin2Token = await LoginAsync(client, seed.Admin2);
        var now = DateTime.UtcNow;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin1Token);

        // 2. Reject unsupported beneficiary group and CHILD_UNDER_6 on periodic pass (FR-25.01, BR-021).
        await AssertProblemAsync(await client.PostAsJsonAsync("/api/v1/admin/eligibility-policies", new
        {
            code = $"BADG-{seed.Suffix}", beneficiaryGroup = "VIP", discountType = "PERCENT", discountValue = 50m,
            eligibleProductCodes = Array.Empty<string>(), requiredEvidenceTypes = Array.Empty<string>(),
            effectiveFrom = now, effectiveTo = (DateTime?)null, reason = "unsupported"
        }), HttpStatusCode.BadRequest, "UNSUPPORTED_ELIGIBILITY_GROUP");

        await AssertProblemAsync(await client.PostAsJsonAsync("/api/v1/admin/eligibility-policies", new
        {
            code = $"BADC-{seed.Suffix}", beneficiaryGroup = "CHILD_UNDER_6", discountType = "FREE", discountValue = 100m,
            eligibleProductCodes = new[] { seed.DayCode }, requiredEvidenceTypes = new[] { "BIRTH_CERT" },
            effectiveFrom = now, effectiveTo = (DateTime?)null, reason = "child on periodic"
        }), HttpStatusCode.BadRequest, "CHILD_SINGLE_TRIP_ONLY");

        // 3. Create valid ELDERLY 20% policy with Idempotency-Key (replay + conflict).
        var epBody = new
        {
            code = $"ELD-{seed.Suffix}", beneficiaryGroup = "ELDERLY", discountType = "PERCENT", discountValue = 20m,
            eligibleProductCodes = Array.Empty<string>(), requiredEvidenceTypes = new[] { "CITIZEN_ID_DEMO" },
            effectiveFrom = now.AddMinutes(-5), effectiveTo = (DateTime?)null, reason = "BR-071 elderly 20%"
        };
        var epRes = await SendWithIdemAsync(client, HttpMethod.Post, "/api/v1/admin/eligibility-policies", epBody, $"ep1-{seed.Suffix}");
        Assert.Equal(HttpStatusCode.Created, epRes.StatusCode);
        using var epDoc = JsonDocument.Parse(await epRes.Content.ReadAsStringAsync());
        var epId = epDoc.RootElement.GetProperty("id").GetGuid();

        var epReplay = await SendWithIdemAsync(client, HttpMethod.Post, "/api/v1/admin/eligibility-policies", epBody, $"ep1-{seed.Suffix}");
        Assert.Equal(HttpStatusCode.Created, epReplay.StatusCode);
        Assert.Equal("true", epReplay.Headers.GetValues("X-Idempotency-Replayed").Single());

        await AssertProblemAsync(
            await SendWithIdemAsync(client, HttpMethod.Post, "/api/v1/admin/eligibility-policies", epBody with { discountValue = 15m }, $"ep1-{seed.Suffix}"),
            HttpStatusCode.Conflict, "IDEMPOTENCY_KEY_CONFLICT");

        // 4. Maker-checker blocks creator self-approval (409); Admin 2 approves & publishes (200).
        await AssertProblemAsync(
            await client.PostAsJsonAsync($"/api/v1/admin/eligibility-policies/{epId}/approve", new { reason = "self" }),
            HttpStatusCode.Conflict, "MAKER_CHECKER_VIOLATION");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin2Token);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/v1/admin/eligibility-policies/{epId}/approve", new { reason = "ok" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/v1/admin/eligibility-policies/{epId}/publish", new { reason = "go" })).StatusCode);

        // 5. Verify audit trail and no PII/secret leakage.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<IOperatorContext>().CurrentOperatorId = seed.OperatorId;
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var audits = await db.AuditLogs.AsNoTracking()
                .Where(a => a.EntityType == "ELIGIBILITY_POLICY" && a.EntityId == epId.ToString()).ToListAsync();
            Assert.Equal(3, audits.Count); // CREATE, APPROVE, PUBLISH
        }

        var logs = string.Join(Environment.NewLine, factory.TestLogs.Messages);
        Assert.DoesNotContain(seed.Admin1.Email, logs, StringComparison.OrdinalIgnoreCase);
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

    private async Task<SeededEligAdmin> SeedAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        var opId = Guid.NewGuid();
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IOperatorContext>().CurrentOperatorId = opId;
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        db.Operators.Add(new Operator { Id = opId, Code = $"AE-{suffix}", Name = $"Admin Elig Op {suffix}", Status = "ACTIVE" });
        await db.SaveChangesAsync();

        var passenger = await CreateUserAsync(users, roles, db, opId, "Passenger", "PASSENGER", false, $"aep-{suffix}");
        var admin1 = await CreateUserAsync(users, roles, db, opId, "Admin", "ADMIN", true, $"aea1-{suffix}");
        var admin2 = await CreateUserAsync(users, roles, db, opId, "Admin", "ADMIN", true, $"aea2-{suffix}");
        var dayProduct = new TicketProduct
        {
            OperatorId = opId, Code = $"DAY-{suffix}", Name = "Day pass", ProductType = "DAY",
            DurationValue = 1, DurationUnit = "DAY", ExpiryRule = "NEXT_LOCAL_MIDNIGHT", CreatedByUserId = admin1.Id
        };
        db.TicketProducts.Add(dayProduct);
        await db.SaveChangesAsync();
        return new SeededEligAdmin(opId, suffix, dayProduct.Code, passenger, admin1, admin2);
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
    private sealed record SeededEligAdmin(Guid OperatorId, string Suffix, string DayCode, ActorCreds Passenger, ActorCreds Admin1, ActorCreds Admin2);
}
