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
public sealed class AdminTripEndpointsTests(MetroWebApplicationFactory factory)
{
    [PostgreSqlFact]
    [Trait("Category", "PostgreSQL")]
    public async Task Admin_trips_enforce_auth_idempotency_auto_stops_trainset_overlap_BR056_and_audit()
    {
        var seed = await SeedAsync();
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/admin/trips")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await LoginAsync(client, seed.Passenger));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/admin/trips")).StatusCode);

        var adminToken = await LoginAsync(client, seed.Admin);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);
        var tripStart = now.AddHours(1);
        var tripEnd = tripStart.AddMinutes(30);

        // 0. Reject attaching non-PUBLISHED (DRAFT) load profile on trip creation (UC37 maker-checker guard).
        await AssertProblemAsync(
            await client.PostAsJsonAsync("/api/v1/admin/trips", new
            {
                scheduleVersionId = seed.ScheduleId, lineId = seed.LineId, tripCode = $"TRD-{seed.Suffix}", serviceDate = today,
                direction = (short)0, plannedStartAt = tripStart, plannedEndAt = tripEnd,
                loadProfileId = seed.DraftLoadProfileId, reason = "Draft profile should be rejected"
            }),
            HttpStatusCode.Conflict, "LOAD_PROFILE_NOT_PUBLISHED");

        // 1. Create Trip 1 with Idempotency-Key + auto-generated stop times from travel_time_seconds (180s) & trainset 1.
        var t1Body = new
        {
            scheduleVersionId = seed.ScheduleId, lineId = seed.LineId, tripCode = $"TR1-{seed.Suffix}", serviceDate = today,
            direction = (short)0, plannedStartAt = tripStart, plannedEndAt = tripEnd,
            trainsetId = seed.Trainset1Id, reason = "Morning trip 1"
        };
        var trip1Res = await SendWithIdemAsync(client, HttpMethod.Post, "/api/v1/admin/trips", t1Body, $"tr1-{seed.Suffix}");
        Assert.Equal(HttpStatusCode.Created, trip1Res.StatusCode);
        using var t1Doc = JsonDocument.Parse(await trip1Res.Content.ReadAsStringAsync());
        var trip1Id = t1Doc.RootElement.GetProperty("id").GetGuid();
        var stops = t1Doc.RootElement.GetProperty("stopTimes").EnumerateArray().ToArray();
        Assert.Equal(2, stops.Length);
        Assert.Equal(180, (int)(stops[1].GetProperty("plannedArrivalAt").GetDateTime() - stops[0].GetProperty("plannedArrivalAt").GetDateTime()).TotalSeconds);
        Assert.Equal(seed.Trainset1Id, t1Doc.RootElement.GetProperty("activeAllocation").GetProperty("trainsetId").GetGuid());

        var replay = await SendWithIdemAsync(client, HttpMethod.Post, "/api/v1/admin/trips", t1Body, $"tr1-{seed.Suffix}");
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal("true", replay.Headers.GetValues("X-Idempotency-Replayed").Single());

        // 2. Overlapping allocation of same Trainset 1 on Trip 2 blocked by PostgreSQL GiST -> 409 TRAINSET_ALLOCATION_OVERLAP (BR-053).
        await AssertProblemAsync(
            await client.PostAsJsonAsync("/api/v1/admin/trips", new
            {
                scheduleVersionId = seed.ScheduleId, lineId = seed.LineId, tripCode = $"TR2-{seed.Suffix}", serviceDate = today,
                direction = (short)0, plannedStartAt = tripStart.AddMinutes(10), plannedEndAt = tripEnd.AddMinutes(10),
                trainsetId = seed.Trainset1Id, reason = "Overlapping trainset"
            }),
            HttpStatusCode.Conflict, "TRAINSET_ALLOCATION_OVERLAP");

        // 3. IN_SERVICE + actual arrival; updating only actualDepartureAt must still audit changed_fields & hash diff.
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/v1/admin/trips/{trip1Id}", new
        {
            status = "IN_SERVICE", boardingStationId = seed.S1Id, boardingStatus = "OPEN",
            actualArrivalAt = tripStart, reason = "Arrived S1"
        })).StatusCode);

        await AssertProblemAsync(
            await client.PutAsJsonAsync($"/api/v1/admin/trips/{trip1Id}", new
            {
                status = "IN_SERVICE", boardingStationId = seed.S1Id, boardingStatus = "OPEN",
                actualDepartureAt = tripStart.AddMinutes(-1), reason = "Invalid departure before arrival"
            }),
            HttpStatusCode.BadRequest, "INVALID_STOP_TIME");

        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/v1/admin/trips/{trip1Id}", new
        {
            status = "IN_SERVICE", boardingStationId = seed.S1Id, boardingStatus = "OPEN",
            actualDepartureAt = tripStart.AddMinutes(2), reason = "Departed S1"
        })).StatusCode);

        // 3b. BR-056 blocks swapping trainset while boarding OPEN; DRAFT/APPROVED load profiles rejected; PUBLISHED succeeds once PAUSED.
        await AssertProblemAsync(
            await client.PostAsJsonAsync($"/api/v1/admin/trips/{trip1Id}/allocations", new { trainsetId = seed.Trainset2Id, reason = "Swap while open" }),
            HttpStatusCode.Conflict, "BOARDING_MUST_PAUSE_FOR_SWAP");

        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/v1/admin/trips/{trip1Id}",
            new { status = "IN_SERVICE", boardingStationId = seed.S1Id, boardingStatus = "PAUSED", reason = "Pause for swap" })).StatusCode);

        await AssertProblemAsync(
            await client.PutAsJsonAsync($"/api/v1/admin/trips/{trip1Id}",
                new { status = "IN_SERVICE", boardingStationId = seed.S1Id, boardingStatus = "PAUSED", loadProfileId = seed.DraftLoadProfileId, reason = "Draft profile" }),
            HttpStatusCode.Conflict, "LOAD_PROFILE_NOT_PUBLISHED");

        await AssertProblemAsync(
            await client.PutAsJsonAsync($"/api/v1/admin/trips/{trip1Id}",
                new { status = "IN_SERVICE", boardingStationId = seed.S1Id, boardingStatus = "PAUSED", loadProfileId = seed.ApprovedLoadProfileId, reason = "Unpublished profile" }),
            HttpStatusCode.Conflict, "LOAD_PROFILE_NOT_PUBLISHED");

        var attachPublishedRes = await client.PutAsJsonAsync($"/api/v1/admin/trips/{trip1Id}",
            new { status = "IN_SERVICE", boardingStationId = seed.S1Id, boardingStatus = "PAUSED", loadProfileId = seed.PublishedLoadProfileId, reason = "Attach published profile" });
        Assert.Equal(HttpStatusCode.OK, attachPublishedRes.StatusCode);
        using (var attachedDoc = JsonDocument.Parse(await attachPublishedRes.Content.ReadAsStringAsync()))
            Assert.Equal(seed.PublishedLoadProfileId, attachedDoc.RootElement.GetProperty("loadProfileId").GetGuid());

        await AssertProblemAsync(
            await client.PostAsJsonAsync($"/api/v1/admin/trips/{trip1Id}/allocations",
                new { trainsetId = seed.Trainset2Id, loadProfileId = seed.DraftLoadProfileId, reason = "Draft on alloc" }),
            HttpStatusCode.Conflict, "LOAD_PROFILE_NOT_PUBLISHED");

        var swapRes = await SendWithIdemAsync(client, HttpMethod.Post, $"/api/v1/admin/trips/{trip1Id}/allocations",
            new { trainsetId = seed.Trainset2Id, loadProfileId = seed.PublishedLoadProfileId, reason = "Swapped to DT02" }, $"swap-{seed.Suffix}");
        Assert.Equal(HttpStatusCode.OK, swapRes.StatusCode);

        // 4. Cancellation requires non-empty reason (400), then cancelling succeeds and blocks resurrecting (409).
        await AssertProblemAsync(
            await client.PutAsJsonAsync($"/api/v1/admin/trips/{trip1Id}", new { status = "CANCELLED", cancellationReason = "   " }),
            HttpStatusCode.BadRequest, "CANCELLATION_REASON_REQUIRED");
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/v1/admin/trips/{trip1Id}",
            new { status = "CANCELLED", cancellationReason = "Power fault", reason = "Cancelled" })).StatusCode);
        await AssertProblemAsync(
            await client.PutAsJsonAsync($"/api/v1/admin/trips/{trip1Id}", new { status = "IN_SERVICE", reason = "Resurrect" }),
            HttpStatusCode.Conflict, "INVALID_TRIP_TRANSITION");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<IOperatorContext>().CurrentOperatorId = seed.OperatorId;
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var audits = await db.AuditLogs.AsNoTracking()
                .Where(a => a.EntityType == "PLANNED_TRIP" && a.EntityId == trip1Id.ToString()).ToListAsync();
            Assert.True(audits.Count >= 5);

            var arrivalAudit = audits.Single(a => a.Action == "UPDATE_PLANNED_TRIP" && a.Reason == "Arrived S1");
            Assert.Contains("actual_arrival_at", arrivalAudit.ChangedFields);
            Assert.DoesNotContain("actual_departure_at", arrivalAudit.ChangedFields);
            Assert.NotEqual(arrivalAudit.BeforeHash, arrivalAudit.AfterHash);

            var departAudit = audits.Single(a => a.Action == "UPDATE_PLANNED_TRIP" && a.Reason == "Departed S1");
            Assert.Contains("actual_departure_at", departAudit.ChangedFields);
            Assert.DoesNotContain("actual_arrival_at", departAudit.ChangedFields);
            Assert.NotNull(departAudit.BeforeHash);
            Assert.NotNull(departAudit.AfterHash);
            Assert.NotEqual(departAudit.BeforeHash, departAudit.AfterHash);

            var actualStop = await db.ActualStopTimes.AsNoTracking()
                .SingleAsync(s => s.StationId == seed.S1Id && s.ActualTrip.PlannedTripId == trip1Id);
            Assert.NotNull(actualStop.ActualArrivalAt);
            Assert.NotNull(actualStop.ActualDepartureAt);
        }

        var logs = string.Join(Environment.NewLine, factory.TestLogs.Messages);
        Assert.DoesNotContain(seed.Admin.Email, logs, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(seed.Admin.Password, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(adminToken, logs, StringComparison.Ordinal);
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
        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IOperatorContext>().CurrentOperatorId = opId;
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        db.Operators.Add(new Operator { Id = opId, Code = $"AT-{suffix}", Name = $"Admin Trip {suffix}", Status = "ACTIVE" });
        await db.SaveChangesAsync();

        var passenger = await CreateUserAsync(users, roles, db, opId, "Passenger", "PASSENGER", false, $"atp-{suffix}");
        var admin = await CreateUserAsync(users, roles, db, opId, "Admin", "ADMIN", true, $"ata-{suffix}");
        var line = new Line { OperatorId = opId, Code = $"L-{suffix}", Name = "Line 1", Status = "ACTIVE" };
        var s1 = new Station { OperatorId = opId, Code = $"S1-{suffix}", Name = "Station 1", Status = "ACTIVE" };
        var s2 = new Station { OperatorId = opId, Code = $"S2-{suffix}", Name = "Station 2", Status = "ACTIVE" };
        var cal = new ServiceCalendar { OperatorId = opId, Code = $"CAL-{suffix}", Name = "Weekday", DayType = "WEEKDAY", DaysOfWeek = [1, 2, 3, 4, 5], EffectiveFrom = today.AddDays(-1), Status = "PUBLISHED", CreatedByUserId = admin.Id };
        db.AddRange(line, s1, s2, cal);
        await db.SaveChangesAsync();

        var sch = new ScheduleVersion { OperatorId = opId, LineId = line.Id, ServiceCalendarId = cal.Id, Version = 1, Code = $"SCH-{suffix}", Status = "PUBLISHED", EffectiveFrom = now.AddDays(-1), CreatedByUserId = admin.Id };
        db.AddRange(sch,
            new LineStation { OperatorId = opId, LineId = line.Id, StationId = s1.Id, Direction = 0, SequenceNo = 1, DistanceFromOriginM = 0, TravelTimeSeconds = 180 },
            new LineStation { OperatorId = opId, LineId = line.Id, StationId = s2.Id, Direction = 0, SequenceNo = 2, DistanceFromOriginM = 1500, TravelTimeSeconds = 180 });
        await db.SaveChangesAsync();

        var ts1 = Guid.NewGuid();
        var ts2 = Guid.NewGuid();
        var draftProfileId = Guid.NewGuid();
        var approvedProfileId = Guid.NewGuid();
        var publishedProfileId = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO metro.trainsets (id, operator_id, code, depot_code, status)
            VALUES ({ts1}, {opId}, {$"DT01-{suffix}"}, 'DEPOT-1', 'AVAILABLE'),
                   ({ts2}, {opId}, {$"DT02-{suffix}"}, 'DEPOT-1', 'AVAILABLE')
            """);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO metro.capacity_profiles (id, operator_id, code, version, line_id, trainset_id, seated_capacity, standing_capacity, max_load_kg, status, effective_from, created_by_user_id)
            VALUES ({draftProfileId}, {opId}, {$"LP-D-{suffix}"}, 1, {line.Id}, {ts1}, 0, 0, 18000.0, 'DRAFT', {now.AddDays(-1)}, {admin.Id}),
                   ({approvedProfileId}, {opId}, {$"LP-A-{suffix}"}, 1, {line.Id}, {ts1}, 0, 0, 18000.0, 'APPROVED', {now.AddDays(-1)}, {admin.Id}),
                   ({publishedProfileId}, {opId}, {$"LP-P-{suffix}"}, 1, {line.Id}, {ts2}, 0, 0, 18000.0, 'PUBLISHED', {now.AddDays(-1)}, {admin.Id})
            """);
        return new SeededData(opId, suffix, line.Id, sch.Id, s1.Id, s2.Id, ts1, ts2, draftProfileId, approvedProfileId, publishedProfileId, passenger, admin);
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
    private sealed record SeededData(
        Guid OperatorId, string Suffix, Guid LineId, Guid ScheduleId, Guid S1Id, Guid S2Id,
        Guid Trainset1Id, Guid Trainset2Id, Guid DraftLoadProfileId, Guid ApprovedLoadProfileId, Guid PublishedLoadProfileId,
        ActorCreds Passenger, ActorCreds Admin);
}
