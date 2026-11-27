using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Metro.Application.Abstractions.MultiTenancy;
using Metro.Domain.Entities.Identity;
using Metro.Domain.Entities.Network;
using Metro.Infrastructure.Persistence.Context;
using Metro.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Metro.IntegrationTests.Endpoints;

[Collection(DatabaseIntegrationCollection.Name)]
public sealed class AdminScheduleEndpointsTests(MetroWebApplicationFactory factory)
{
    [PostgreSqlFact]
    [Trait("Category", "PostgreSQL")]
    public async Task Admin_schedules_enforce_auth_idempotency_maker_checker_gist_overlap_and_audit()
    {
        var seed = await SeedAsync();
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/admin/schedules")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await LoginAsync(client, seed.Passenger));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/admin/schedules")).StatusCode);

        var admin1Token = await LoginAsync(client, seed.Admin1);
        var admin2Token = await LoginAsync(client, seed.Admin2);
        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin1Token);
        var calRes = await SendWithIdemAsync(client, HttpMethod.Post, "/api/v1/admin/schedules/calendars", new
        {
            code = $"CAL-{seed.Suffix}", name = "Weekday", dayType = "WEEKDAY", daysOfWeek = new short[] { 1, 2, 3, 4, 5 },
            effectiveFrom = today.AddDays(-1), effectiveTo = today.AddDays(30), priority = (short)10, status = "PUBLISHED", reason = "Init cal"
        }, $"cal-{seed.Suffix}");
        Assert.Equal(HttpStatusCode.Created, calRes.StatusCode);
        var calId = JsonDocument.Parse(await calRes.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();

        var schBody = new { lineId = seed.LineId, serviceCalendarId = calId, code = $"SCH1-{seed.Suffix}", effectiveFrom = now.AddMinutes(-5), effectiveTo = now.AddDays(15), reason = "v1" };
        var sch1Res = await SendWithIdemAsync(client, HttpMethod.Post, "/api/v1/admin/schedules", schBody, $"sch1-{seed.Suffix}");
        Assert.Equal(HttpStatusCode.Created, sch1Res.StatusCode);
        var sch1Id = JsonDocument.Parse(await sch1Res.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();

        var sch1Replay = await SendWithIdemAsync(client, HttpMethod.Post, "/api/v1/admin/schedules", schBody, $"sch1-{seed.Suffix}");
        Assert.Equal(HttpStatusCode.Created, sch1Replay.StatusCode);
        Assert.Equal("true", sch1Replay.Headers.GetValues("X-Idempotency-Replayed").Single());

        await AssertProblemAsync(
            await client.PostAsJsonAsync($"/api/v1/admin/schedules/{sch1Id}/approve", new { reason = "Self" }),
            HttpStatusCode.Conflict, "MAKER_CHECKER_VIOLATION");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin2Token);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/v1/admin/schedules/{sch1Id}/approve", new { reason = "OK" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/v1/admin/schedules/{sch1Id}/publish", new { reason = "Go live" })).StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin1Token);
        var sch2Res = await client.PostAsJsonAsync("/api/v1/admin/schedules",
            new { lineId = seed.LineId, serviceCalendarId = calId, code = $"SCH2-{seed.Suffix}", effectiveFrom = now.AddDays(2), effectiveTo = now.AddDays(20), reason = "overlap" });
        Assert.Equal(HttpStatusCode.Created, sch2Res.StatusCode);
        var sch2Id = JsonDocument.Parse(await sch2Res.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin2Token);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/v1/admin/schedules/{sch2Id}/approve", new { reason = "OK" })).StatusCode);
        await AssertProblemAsync(
            await client.PostAsJsonAsync($"/api/v1/admin/schedules/{sch2Id}/publish", new { reason = "Overlap publish" }),
            HttpStatusCode.Conflict, "SCHEDULE_WINDOW_OVERLAP");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<IOperatorContext>().CurrentOperatorId = seed.OperatorId;
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var audits = await db.AuditLogs.AsNoTracking()
                .Where(a => a.EntityType == "SCHEDULE_VERSION" && a.EntityId == sch1Id.ToString()).ToListAsync();
            Assert.Equal(3, audits.Count);
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
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IOperatorContext>().CurrentOperatorId = opId;
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        db.Operators.Add(new Operator { Id = opId, Code = $"AS-{suffix}", Name = $"Admin Sched {suffix}", Status = "ACTIVE" });
        await db.SaveChangesAsync();

        var passenger = await CreateUserAsync(users, roles, db, opId, "Passenger", "PASSENGER", false, $"asp-{suffix}");
        var admin1 = await CreateUserAsync(users, roles, db, opId, "Admin", "ADMIN", true, $"asa1-{suffix}");
        var admin2 = await CreateUserAsync(users, roles, db, opId, "Admin", "ADMIN", true, $"asa2-{suffix}");
        var line = new Line { OperatorId = opId, Code = $"L-{suffix}", Name = "Line 1", Status = "ACTIVE" };
        db.Lines.Add(line);
        await db.SaveChangesAsync();
        return new SeededData(opId, suffix, line.Id, passenger, admin1, admin2);
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
    private sealed record SeededData(Guid OperatorId, string Suffix, Guid LineId, ActorCreds Passenger, ActorCreds Admin1, ActorCreds Admin2);
}
