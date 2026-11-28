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
public sealed class AdminTripLoadEndpointsTests(MetroWebApplicationFactory factory)
{
    [PostgreSqlFact]
    [Trait("Category", "PostgreSQL")]
    public async Task Admin_trip_load_and_eta_enforce_auth_max_load_kg_AC_X18_BR056_catalog_sync_and_audit()
    {
        var seed = await SeedAsync();
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/admin/trip-load")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await LoginAsync(client, seed.Passenger));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/admin/trip-load")).StatusCode);

        var admin1Token = await LoginAsync(client, seed.Admin1);
        var admin2Token = await LoginAsync(client, seed.Admin2);
        var now = DateTime.UtcNow;

        // 1. Create trainset with 3 cars x 6,000.0 kg = 18,000.0 kg (BR-053/054).
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin1Token);
        var tsBody = new
        {
            code = $"DT01-{seed.Suffix}", depotCode = "DEPOT-1", status = "AVAILABLE",
            cars = new[]
            {
                new { code = $"C1-{seed.Suffix}", sequenceNo = (short)1, carType = "TC", maxLoadKg = 6000.0m },
                new { code = $"C2-{seed.Suffix}", sequenceNo = (short)2, carType = "M", maxLoadKg = 6000.0m },
                new { code = $"C3-{seed.Suffix}", sequenceNo = (short)3, carType = "TC", maxLoadKg = 6000.0m },
            },
            reason = "Init trainset"
        };
        var tsRes = await SendWithIdemAsync(client, HttpMethod.Post, "/api/v1/admin/trip-load/trainsets", tsBody, $"ts-{seed.Suffix}");
        Assert.Equal(HttpStatusCode.Created, tsRes.StatusCode);
        var tsId = JsonDocument.Parse(await tsRes.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();

        // 2. Reject profile where sum of car limits (18,000) exceeds profile maxLoadKg (15,000).
        await AssertProblemAsync(
            await client.PostAsJsonAsync("/api/v1/admin/trip-load/profiles", new
            {
                code = $"LP-BAD-{seed.Suffix}", lineId = seed.LineId, trainsetId = tsId, maxLoadKg = 15000.0m,
                effectiveFrom = now.AddMinutes(-5), effectiveTo = now.AddDays(30), reason = "Too small"
            }),
            HttpStatusCode.BadRequest, "LOAD_PROFILE_CAR_LIMIT_EXCEEDED");

        // 3. Create valid 18,000.0 kg profile, enforce maker-checker on approve, publish with Admin 2, attach to trip.
        var lpRes = await SendWithIdemAsync(client, HttpMethod.Post, "/api/v1/admin/trip-load/profiles", new
        {
            code = $"LP-{seed.Suffix}", lineId = seed.LineId, trainsetId = tsId, maxLoadKg = 18000.0m,
            effectiveFrom = now.AddMinutes(-5), effectiveTo = now.AddDays(30), reason = "18t limit"
        }, $"lp-{seed.Suffix}");
        Assert.Equal(HttpStatusCode.Created, lpRes.StatusCode);
        var lpId = JsonDocument.Parse(await lpRes.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();

        await AssertProblemAsync(
            await client.PostAsJsonAsync($"/api/v1/admin/trip-load/profiles/{lpId}/approve", new { reason = "Self" }),
            HttpStatusCode.Conflict, "MAKER_CHECKER_VIOLATION");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin2Token);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/v1/admin/trip-load/profiles/{lpId}/approve", new { reason = "OK" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/v1/admin/trip-load/profiles/{lpId}/publish", new { reason = "Live" })).StatusCode);

        // Attach profile to trip via PR 4 endpoint.
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/v1/admin/trips/{seed.TripId}",
            new { status = "READY", loadProfileId = lpId, reason = "Attach 18t profile" })).StatusCode);

        // 4. AC-X18 boundary tests: NO_DATA -> 17999.9 (AVAILABLE) -> 18000.0 (FULL) -> 18000.1 (FULL); reject negative & 2 decimals.
        var noDataRes = await client.GetAsync($"/api/v1/admin/trip-load?tripId={seed.TripId}");
        Assert.Equal(HttpStatusCode.OK, noDataRes.StatusCode);
        Assert.Equal("NO_DATA", JsonDocument.Parse(await noDataRes.Content.ReadAsStringAsync()).RootElement[0].GetProperty("loadStatus").GetString());

        await AssertProblemAsync(await client.PostAsJsonAsync("/api/v1/admin/trip-load/observations",
            new { plannedTripId = seed.TripId, observedLoadKg = 17999.95m, note = "2 decimals" }), HttpStatusCode.BadRequest, "INVALID_LOAD_KG");
        await AssertProblemAsync(await client.PostAsJsonAsync("/api/v1/admin/trip-load/observations",
            new { plannedTripId = seed.TripId, observedLoadKg = -1m, note = "Negative" }), HttpStatusCode.BadRequest, "INVALID_LOAD_KG");

        var obs1Body = new { plannedTripId = seed.TripId, observedLoadKg = 17999.9m, observedAt = now.AddSeconds(-30), note = "Near full" };
        var obs1 = await SendWithIdemAsync(client, HttpMethod.Post, "/api/v1/admin/trip-load/observations", obs1Body, $"obs1-{seed.Suffix}");
        Assert.Equal(HttpStatusCode.Created, obs1.StatusCode);
        Assert.Equal("AVAILABLE", JsonDocument.Parse(await obs1.Content.ReadAsStringAsync()).RootElement.GetProperty("loadStatus").GetString());

        var obs1Replay = await SendWithIdemAsync(client, HttpMethod.Post, "/api/v1/admin/trip-load/observations", obs1Body, $"obs1-{seed.Suffix}");
        Assert.Equal("true", obs1Replay.Headers.GetValues("X-Idempotency-Replayed").Single());

        var obs2 = await client.PostAsJsonAsync("/api/v1/admin/trip-load/observations",
            new { plannedTripId = seed.TripId, observedLoadKg = 18000.0m, observedAt = now.AddSeconds(-15), note = "At limit" });
        Assert.Equal(HttpStatusCode.Created, obs2.StatusCode);
        Assert.Equal("FULL", JsonDocument.Parse(await obs2.Content.ReadAsStringAsync()).RootElement.GetProperty("loadStatus").GetString());

        var obs3 = await client.PostAsJsonAsync("/api/v1/admin/trip-load/observations",
            new { plannedTripId = seed.TripId, observedLoadKg = 18000.1m, observedAt = now.AddSeconds(-5), note = "Overload warning" });
        Assert.Equal(HttpStatusCode.Created, obs3.StatusCode);
        Assert.Equal("FULL", JsonDocument.Parse(await obs3.Content.ReadAsStringAsync()).RootElement.GetProperty("loadStatus").GetString());

        // 5. BR-056: updating trainset car limits later does NOT retroactively change assigned trip's max_load_kg (still 18000.0).
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/v1/admin/trip-load/trainsets/{tsId}", new
        {
            depotCode = "DEPOT-1", status = "AVAILABLE",
            cars = new[] { new { code = $"C1N-{seed.Suffix}", sequenceNo = (short)1, carType = "TC", maxLoadKg = 5000.0m } },
            reason = "Edit master car"
        })).StatusCode);
        var statusAfterMasterEdit = await client.GetAsync($"/api/v1/admin/trip-load?tripId={seed.TripId}");
        Assert.Equal(18000.0m, JsonDocument.Parse(await statusAfterMasterEdit.Content.ReadAsStringAsync()).RootElement[0].GetProperty("maxLoadKg").GetDecimal());

        // 6. Manual ETA update -> immediately reflected in BE-05 /api/v1/service-status after cache invalidation!
        using var publicClient = factory.CreateClient();
        var preStatus = await publicClient.GetAsync($"/api/v1/service-status?operatorCode={seed.OperatorCode}");
        Assert.Equal(HttpStatusCode.OK, preStatus.StatusCode);
        Assert.False(JsonDocument.Parse(await preStatus.Content.ReadAsStringAsync()).RootElement.GetProperty("etaAvailable").GetBoolean());

        var etaRes = await SendWithIdemAsync(client, HttpMethod.Post, "/api/v1/admin/trip-load/eta", new
        {
            plannedTripId = seed.TripId, stationId = seed.StationId,
            expectedArrivalAt = now.AddMinutes(8), expectedDepartureAt = now.AddMinutes(9), note = "Approaching S1"
        }, $"eta-{seed.Suffix}");
        Assert.Equal(HttpStatusCode.Created, etaRes.StatusCode);

        var postStatus = await publicClient.GetAsync($"/api/v1/service-status?operatorCode={seed.OperatorCode}");
        Assert.Equal(HttpStatusCode.OK, postStatus.StatusCode);
        using (var postDoc = JsonDocument.Parse(await postStatus.Content.ReadAsStringAsync()))
        {
            Assert.True(postDoc.RootElement.GetProperty("etaAvailable").GetBoolean());
            var etaItem = Assert.Single(postDoc.RootElement.GetProperty("etaUpdates").EnumerateArray());
            Assert.Equal("UPDATED", etaItem.GetProperty("etaStatus").GetString());
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<IOperatorContext>().CurrentOperatorId = seed.OperatorId;
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var audits = await db.AuditLogs.AsNoTracking()
                .Where(a => a.EntityType == "PLANNED_TRIP" && a.EntityId == seed.TripId.ToString()).ToListAsync();
            Assert.True(audits.Count >= 5);
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
        var opCode = $"AL-{suffix}";
        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IOperatorContext>().CurrentOperatorId = opId;
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        db.Operators.Add(new Operator { Id = opId, Code = opCode, Name = $"Admin Load {suffix}", Status = "ACTIVE" });
        await db.SaveChangesAsync();

        var passenger = await CreateUserAsync(users, roles, db, opId, "Passenger", "PASSENGER", false, $"alp-{suffix}");
        var admin1 = await CreateUserAsync(users, roles, db, opId, "Admin", "ADMIN", true, $"ala1-{suffix}");
        var admin2 = await CreateUserAsync(users, roles, db, opId, "Admin", "ADMIN", true, $"ala2-{suffix}");
        var line = new Line { OperatorId = opId, Code = $"L-{suffix}", Name = "Line 1", Status = "ACTIVE" };
        var s1 = new Station { OperatorId = opId, Code = $"S1-{suffix}", Name = "Station 1", Status = "ACTIVE" };
        var cal = new ServiceCalendar { OperatorId = opId, Code = $"CAL-{suffix}", Name = "Daily", DayType = "WEEKDAY", DaysOfWeek = [1, 2, 3, 4, 5], EffectiveFrom = today.AddDays(-1), Status = "PUBLISHED", CreatedByUserId = admin1.Id };
        db.AddRange(line, s1, cal);
        await db.SaveChangesAsync();

        var sch = new ScheduleVersion { OperatorId = opId, LineId = line.Id, ServiceCalendarId = cal.Id, Version = 1, Code = $"SCH-{suffix}", Status = "PUBLISHED", EffectiveFrom = now.AddDays(-1), CreatedByUserId = admin1.Id };
        db.ScheduleVersions.Add(sch);
        await db.SaveChangesAsync();

        var trip = new PlannedTrip { OperatorId = opId, ScheduleVersionId = sch.Id, LineId = line.Id, TripCode = $"TR-{suffix}", ServiceDate = today, Direction = 0, PlannedStartAt = now.AddMinutes(-10), PlannedEndAt = now.AddHours(1), Status = "PLANNED" };
        db.PlannedTrips.Add(trip);
        db.PlannedStopTimes.Add(new PlannedStopTime { OperatorId = opId, PlannedTripId = trip.Id, StationId = s1.Id, SequenceNo = 1, PlannedArrivalAt = now.AddMinutes(-5), PlannedDepartureAt = now.AddMinutes(-4), DwellSeconds = 60 });
        await db.SaveChangesAsync();

        return new SeededData(opId, opCode, suffix, line.Id, s1.Id, trip.Id, passenger, admin1, admin2);
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
    private sealed record SeededData(Guid OperatorId, string OperatorCode, string Suffix, Guid LineId, Guid StationId, Guid TripId, ActorCreds Passenger, ActorCreds Admin1, ActorCreds Admin2);
}
