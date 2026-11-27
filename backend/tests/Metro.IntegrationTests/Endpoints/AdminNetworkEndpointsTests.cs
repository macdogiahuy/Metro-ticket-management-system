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
public sealed class AdminNetworkEndpointsTests(MetroWebApplicationFactory factory)
{
    [PostgreSqlFact]
    [Trait("Category", "PostgreSQL")]
    public async Task Admin_lines_and_stations_enforce_auth_idempotency_BR018_timing_cache_invalidation_and_audit()
    {
        var seed = await SeedAsync();
        using var client = factory.CreateClient();

        // 1. Auth checks: 401 unauthenticated, 403 Passenger.
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/admin/lines")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/admin/stations")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await LoginAsync(client, seed.Passenger));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/admin/lines")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/admin/stations")).StatusCode);

        var adminToken = await LoginAsync(client, seed.Admin);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        // 2. Create line with Idempotency-Key + verify replay & 409 conflict.
        var lineBody = new { code = $"T01-{seed.Suffix}", name = "Line T01", status = "ACTIVE", openedOn = "2026-01-01", reason = "Seed T01" };
        var lineRes = await SendWithIdemAsync(client, HttpMethod.Post, "/api/v1/admin/lines", lineBody, $"line-{seed.Suffix}");
        Assert.Equal(HttpStatusCode.Created, lineRes.StatusCode);
        using var lineDoc = JsonDocument.Parse(await lineRes.Content.ReadAsStringAsync());
        var lineId = lineDoc.RootElement.GetProperty("id").GetGuid();

        var lineReplay = await SendWithIdemAsync(client, HttpMethod.Post, "/api/v1/admin/lines", lineBody, $"line-{seed.Suffix}");
        Assert.Equal(HttpStatusCode.Created, lineReplay.StatusCode);
        Assert.Equal("true", lineReplay.Headers.GetValues("X-Idempotency-Replayed").Single());

        await AssertProblemAsync(
            await SendWithIdemAsync(client, HttpMethod.Post, "/api/v1/admin/lines", lineBody with { name = "Different" }, $"line-{seed.Suffix}"),
            HttpStatusCode.Conflict, "IDEMPOTENCY_KEY_CONFLICT");

        // 3. Create 3 stations (with custom minTransferSeconds) + 2nd line for transfer journey verification.
        var s1Id = await CreateStationAsync(client, $"SA-{seed.Suffix}", "Ga A", "ELEVATED", 180, $"sa-{seed.Suffix}");
        var s2Id = await CreateStationAsync(client, $"SC-{seed.Suffix}", "Ga C", "INTERCHANGE", 300, $"sc-{seed.Suffix}");
        var s3Id = await CreateStationAsync(client, $"SF-{seed.Suffix}", "Ga F", "ELEVATED", 180, $"sf-{seed.Suffix}");

        var line2Res = await SendWithIdemAsync(client, HttpMethod.Post, "/api/v1/admin/lines",
            new { code = $"T02-{seed.Suffix}", name = "Line T02", status = "ACTIVE", openedOn = "2026-01-01", reason = "Seed T02" }, $"line2-{seed.Suffix}");
        Assert.Equal(HttpStatusCode.Created, line2Res.StatusCode);
        var line2Id = JsonDocument.Parse(await line2Res.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();

        // Warm BE-05 public catalog cache before linking stations.
        using var publicClient = factory.CreateClient();
        var beforePublic = await publicClient.GetAsync($"/api/v1/lines?operatorCode={seed.OperatorCode}");
        Assert.Equal(HttpStatusCode.OK, beforePublic.StatusCode);

        // 4. Configure line topology with travel_time_seconds (180s) on both lines via interchange station C.
        var setL1 = await SendWithIdemAsync(client, HttpMethod.Put, $"/api/v1/admin/lines/{lineId}/stations", new
        {
            direction = (short)0,
            stops = new[]
            {
                new { stationId = s1Id, sequenceNo = (short)1, distanceFromOriginM = 0, travelTimeSeconds = 180 },
                new { stationId = s2Id, sequenceNo = (short)2, distanceFromOriginM = 1500, travelTimeSeconds = 180 },
            },
            reason = "Set T01 stops"
        }, $"set-l1-{seed.Suffix}");
        Assert.Equal(HttpStatusCode.OK, setL1.StatusCode);

        var setL2 = await SendWithIdemAsync(client, HttpMethod.Put, $"/api/v1/admin/lines/{line2Id}/stations", new
        {
            direction = (short)0,
            stops = new[]
            {
                new { stationId = s2Id, sequenceNo = (short)1, distanceFromOriginM = 0, travelTimeSeconds = 240 },
                new { stationId = s3Id, sequenceNo = (short)2, distanceFromOriginM = 1800, travelTimeSeconds = 240 },
            },
            reason = "Set T02 stops"
        }, $"set-l2-{seed.Suffix}");
        Assert.Equal(HttpStatusCode.OK, setL2.StatusCode);

        // BE-05 catalog cache was invalidated: journey A -> C -> F reflects 180s + 300s transfer + 240s = 720s!
        var journeyRes = await publicClient.GetAsync($"/api/v1/journeys?originStationId={s1Id}&destinationStationId={s3Id}&operatorCode={seed.OperatorCode}");
        Assert.Equal(HttpStatusCode.OK, journeyRes.StatusCode);
        using (var jDoc = JsonDocument.Parse(await journeyRes.Content.ReadAsStringAsync()))
        {
            var opt = Assert.Single(jDoc.RootElement.GetProperty("options").EnumerateArray());
            Assert.Equal(720, opt.GetProperty("estimatedDurationSeconds").GetInt32());
        }

        // Update interchange station C minTransferSeconds to 420s and confirm BE-05 journey updates immediately (840s).
        var updStation = await SendWithIdemAsync(client, HttpMethod.Put, $"/api/v1/admin/stations/{s2Id}", new
        {
            name = "Ga C Interchange", stationType = "INTERCHANGE", latitude = 21.0285m, longitude = 105.8542m,
            minTransferSeconds = 420, status = "ACTIVE", reason = "Adjust transfer time"
        }, $"upd-sc-{seed.Suffix}");
        Assert.Equal(HttpStatusCode.OK, updStation.StatusCode);

        var journeyRes2 = await publicClient.GetAsync($"/api/v1/journeys?originStationId={s1Id}&destinationStationId={s3Id}&operatorCode={seed.OperatorCode}");
        Assert.Equal(HttpStatusCode.OK, journeyRes2.StatusCode);
        using (var jDoc2 = JsonDocument.Parse(await journeyRes2.Content.ReadAsStringAsync()))
            Assert.Equal(840, jDoc2.RootElement.GetProperty("options")[0].GetProperty("estimatedDurationSeconds").GetInt32());

        // 5. BR-018: referenced/active line and station cannot be hard-deleted (409), must be SUSPENDED/CLOSED instead; unreferenced PLANNED draft can be deleted.
        await AssertProblemAsync(await client.DeleteAsync($"/api/v1/admin/lines/{lineId}"), HttpStatusCode.Conflict, "LINE_IN_USE");
        await AssertProblemAsync(await client.DeleteAsync($"/api/v1/admin/stations/{s2Id}"), HttpStatusCode.Conflict, "STATION_IN_USE");

        var suspendLine = await client.PutAsJsonAsync($"/api/v1/admin/lines/{line2Id}",
            new { name = "Line T02", status = "SUSPENDED", openedOn = "2026-01-01", reason = "Maintenance" });
        Assert.Equal(HttpStatusCode.OK, suspendLine.StatusCode);

        var draftLineRes = await client.PostAsJsonAsync("/api/v1/admin/lines",
            new { code = $"DRAFT-{seed.Suffix}", name = "Draft Line", status = "PLANNED", reason = "Temp draft" });
        Assert.Equal(HttpStatusCode.Created, draftLineRes.StatusCode);
        var draftLineId = JsonDocument.Parse(await draftLineRes.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/admin/lines/{draftLineId}?reason=Cleanup")).StatusCode);

        // 6. Verify audit logs persisted and no PII/credentials logged.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<IOperatorContext>().CurrentOperatorId = seed.OperatorId;
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var lineAudits = await db.AuditLogs.AsNoTracking()
                .Where(a => a.EntityType == "LINE" && a.EntityId == lineId.ToString()).ToListAsync();
            Assert.Equal(2, lineAudits.Count); // CREATE_LINE + SET_LINE_STATIONS
        }

        var logs = string.Join(Environment.NewLine, factory.TestLogs.Messages);
        Assert.DoesNotContain(seed.Admin.Email, logs, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(seed.Admin.Password, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(adminToken, logs, StringComparison.Ordinal);
    }

    private static async Task<Guid> CreateStationAsync(HttpClient client, string code, string name, string type, int minTransferSeconds, string idemKey)
    {
        var res = await SendWithIdemAsync(client, HttpMethod.Post, "/api/v1/admin/stations", new
        {
            code, name, stationType = type, latitude = 21.0285m, longitude = 105.8542m,
            minTransferSeconds, status = "ACTIVE", reason = $"Create {code}"
        }, idemKey);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("id").GetGuid();
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

    private async Task<SeededAdmin> SeedAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        var opId = Guid.NewGuid();
        var opCode = $"AN-{suffix}";
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IOperatorContext>().CurrentOperatorId = opId;
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        db.Operators.Add(new Operator { Id = opId, Code = opCode, Name = $"Admin Net {suffix}", Status = "ACTIVE" });
        await db.SaveChangesAsync();

        var passenger = await CreateUserAsync(users, roles, db, opId, "Passenger", "PASSENGER", false, $"anp-{suffix}");
        var admin = await CreateUserAsync(users, roles, db, opId, "Admin", "ADMIN", true, $"ana-{suffix}");
        await db.SaveChangesAsync();
        return new SeededAdmin(opId, opCode, suffix, passenger, admin);
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
    private sealed record SeededAdmin(Guid OperatorId, string OperatorCode, string Suffix, ActorCreds Passenger, ActorCreds Admin);
}
