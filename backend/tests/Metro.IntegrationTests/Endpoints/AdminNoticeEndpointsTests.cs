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
public sealed class AdminNoticeEndpointsTests(MetroWebApplicationFactory factory)
{
    [PostgreSqlFact]
    [Trait("Category", "PostgreSQL")]
    public async Task Admin_notices_enforce_auth_maker_checker_expiry_emergency_fasttrack_catalog_sync_and_audit()
    {
        var seed = await SeedAsync();
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/admin/notices")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await LoginAsync(client, seed.Passenger));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/admin/notices")).StatusCode);

        var admin1Token = await LoginAsync(client, seed.Admin1);
        var admin2Token = await LoginAsync(client, seed.Admin2);
        var now = DateTime.UtcNow;

        // 1. Create standard PUBLIC notice with Idempotency-Key + verify replay & conflict.
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin1Token);
        var createBody = new
        {
            code = $"NTC-{seed.Suffix}", audience = "PUBLIC", title = "Holiday timetable",
            content = "Trains run every 10 minutes from 05:00 to 22:00.", emergencyTemplate = false,
            validFrom = now.AddMinutes(-5), validTo = now.AddHours(4), reason = "Draft holiday notice"
        };
        var createdRes = await SendWithIdemAsync(client, HttpMethod.Post, "/api/v1/admin/notices", createBody, $"ntc-{seed.Suffix}");
        Assert.Equal(HttpStatusCode.Created, createdRes.StatusCode);
        var noticeId = JsonDocument.Parse(await createdRes.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();

        var replay = await SendWithIdemAsync(client, HttpMethod.Post, "/api/v1/admin/notices", createBody, $"ntc-{seed.Suffix}");
        Assert.Equal("true", replay.Headers.GetValues("X-Idempotency-Replayed").Single());

        // 2. Non-emergency notice requires maker-checker (409 self-approve); Admin 2 approves & publishes.
        await AssertProblemAsync(
            await client.PostAsJsonAsync($"/api/v1/admin/notices/{noticeId}/approve", new { reason = "Self" }),
            HttpStatusCode.Conflict, "MAKER_CHECKER_VIOLATION");

        using var publicClient = factory.CreateClient();
        // Warm BE-05 /api/v1/service-status cache before publishing (0 notices).
        var preStatus = await publicClient.GetAsync($"/api/v1/service-status?operatorCode={seed.OperatorCode}");
        Assert.Equal(HttpStatusCode.OK, preStatus.StatusCode);
        Assert.Empty(JsonDocument.Parse(await preStatus.Content.ReadAsStringAsync()).RootElement.GetProperty("notices").EnumerateArray());

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin2Token);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/v1/admin/notices/{noticeId}/approve", new { reason = "Approved" })).StatusCode);
        var pubRes = await SendWithIdemAsync(client, HttpMethod.Post, $"/api/v1/admin/notices/{noticeId}/publish", new { reason = "Publish now" }, $"pub-{seed.Suffix}");
        Assert.Equal(HttpStatusCode.OK, pubRes.StatusCode);
        Assert.True(JsonDocument.Parse(await pubRes.Content.ReadAsStringAsync()).RootElement.GetProperty("isCurrentlyEffective").GetBoolean());

        // Published notice immediately visible in BE-05 /api/v1/service-status after cache invalidation!
        var midStatus = await publicClient.GetAsync($"/api/v1/service-status?operatorCode={seed.OperatorCode}");
        Assert.Equal(HttpStatusCode.OK, midStatus.StatusCode);
        var visible = Assert.Single(JsonDocument.Parse(await midStatus.Content.ReadAsStringAsync()).RootElement.GetProperty("notices").EnumerateArray());
        Assert.Equal($"NTC-{seed.Suffix}", visible.GetProperty("code").GetString());

        // 3. Cancel/Expire notice immediately removes it from BE-05 /api/v1/service-status!
        var expireRes = await client.PostAsJsonAsync($"/api/v1/admin/notices/{noticeId}/cancel", new { reason = "Ended early", expire = true });
        Assert.Equal(HttpStatusCode.OK, expireRes.StatusCode);
        Assert.Equal("EXPIRED", JsonDocument.Parse(await expireRes.Content.ReadAsStringAsync()).RootElement.GetProperty("status").GetString());

        var afterExpire = await publicClient.GetAsync($"/api/v1/service-status?operatorCode={seed.OperatorCode}");
        Assert.Empty(JsonDocument.Parse(await afterExpire.Content.ReadAsStringAsync()).RootElement.GetProperty("notices").EnumerateArray());

        // 4. Reject publishing an already-expired notice (422 NOTICE_EXPIRED per UC31), and verify emergency template fast-track publish.
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin1Token);
        var expDraftRes = await client.PostAsJsonAsync("/api/v1/admin/notices", new
        {
            code = $"NTC-EXP-{seed.Suffix}", audience = "PUBLIC", title = "Stale", content = "Already expired",
            emergencyTemplate = true, validFrom = now.AddHours(-3), validTo = now.AddMinutes(-1), reason = "Stale"
        });
        Assert.Equal(HttpStatusCode.Created, expDraftRes.StatusCode);
        var expDraftId = JsonDocument.Parse(await expDraftRes.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
        await AssertProblemAsync(
            await client.PostAsJsonAsync($"/api/v1/admin/notices/{expDraftId}/publish", new { reason = "Try expired" }),
            HttpStatusCode.UnprocessableEntity, "NOTICE_EXPIRED");

        var emgRes = await client.PostAsJsonAsync("/api/v1/admin/notices", new
        {
            code = $"NTC-EMG-{seed.Suffix}", audience = "PUBLIC", title = "Emergency alert", content = "Temporary delay at Ga C.",
            emergencyTemplate = true, validFrom = now.AddMinutes(-1), validTo = now.AddHours(1), reason = "Emergency"
        });
        Assert.Equal(HttpStatusCode.Created, emgRes.StatusCode);
        var emgId = JsonDocument.Parse(await emgRes.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/v1/admin/notices/{emgId}/publish", new { reason = "Urgent publish" })).StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<IOperatorContext>().CurrentOperatorId = seed.OperatorId;
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var audits = await db.AuditLogs.AsNoTracking()
                .Where(a => a.EntityType == "SERVICE_NOTICE" && a.EntityId == noticeId.ToString()).ToListAsync();
            Assert.Equal(4, audits.Count); // CREATE, APPROVE, PUBLISH, EXPIRE
        }

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

    private async Task<SeededData> SeedAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        var opId = Guid.NewGuid();
        var opCode = $"NT-{suffix}";
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IOperatorContext>().CurrentOperatorId = opId;
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        db.Operators.Add(new Operator { Id = opId, Code = opCode, Name = $"Admin Notice {suffix}", Status = "ACTIVE" });
        await db.SaveChangesAsync();

        var passenger = await CreateUserAsync(users, roles, db, opId, "Passenger", "PASSENGER", false, $"ntp-{suffix}");
        var admin1 = await CreateUserAsync(users, roles, db, opId, "Admin", "ADMIN", true, $"nta1-{suffix}");
        var admin2 = await CreateUserAsync(users, roles, db, opId, "Admin", "ADMIN", true, $"nta2-{suffix}");
        await db.SaveChangesAsync();
        return new SeededData(opId, opCode, suffix, passenger, admin1, admin2);
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
    private sealed record SeededData(Guid OperatorId, string OperatorCode, string Suffix, ActorCreds Passenger, ActorCreds Admin1, ActorCreds Admin2);
}
