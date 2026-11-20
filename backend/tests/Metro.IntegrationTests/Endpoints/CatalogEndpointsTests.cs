using System.Net;
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
public sealed class CatalogEndpointsTests(MetroWebApplicationFactory factory)
{
    [PostgreSqlFact]
    [Trait("Category", "PostgreSQL")]
    public async Task Public_catalog_lines_and_stations_support_filters_200_400_404_and_short_cache()
    {
        var seed = await SeedNetworkAsync();
        using var client = factory.CreateClient();

        var linesRes = await client.GetAsync($"/api/v1/lines?operatorCode={seed.OperatorCode}");
        Assert.Equal(HttpStatusCode.OK, linesRes.StatusCode);
        using var linesDoc = JsonDocument.Parse(await linesRes.Content.ReadAsStringAsync());
        var firstDataAsOf = linesDoc.RootElement.GetProperty("dataAsOf").GetDateTime();
        var lines = linesDoc.RootElement.GetProperty("lines").EnumerateArray().ToArray();
        Assert.Equal(3, lines.Length);

        var line1 = Assert.Single(lines, l => l.GetProperty("code").GetString() == seed.Line1Code);
        var stops = line1.GetProperty("stops").EnumerateArray().ToArray();
        Assert.Equal(4, stops.Length);
        Assert.True(stops[2].GetProperty("isInterchange").GetBoolean());

        // Second identical request hits short-lived cache (identical dataAsOf timestamp).
        await Task.Delay(25);
        var cachedRes = await client.GetAsync($"/api/v1/lines?operatorCode={seed.OperatorCode}");
        Assert.Equal(HttpStatusCode.OK, cachedRes.StatusCode);
        using var cachedDoc = JsonDocument.Parse(await cachedRes.Content.ReadAsStringAsync());
        Assert.Equal(firstDataAsOf, cachedDoc.RootElement.GetProperty("dataAsOf").GetDateTime());

        // Status filter 200 and invalid status 400.
        var activeOnly = await client.GetAsync($"/api/v1/lines?status=active&operatorCode={seed.OperatorCode}");
        Assert.Equal(HttpStatusCode.OK, activeOnly.StatusCode);
        using (var activeDoc = JsonDocument.Parse(await activeOnly.Content.ReadAsStringAsync()))
            Assert.Equal(2, activeDoc.RootElement.GetProperty("lines").GetArrayLength());

        var badStatus = await client.GetAsync($"/api/v1/lines?status=BROKEN&operatorCode={seed.OperatorCode}");
        await AssertProblemAsync(badStatus, HttpStatusCode.BadRequest, "INVALID_STATUS");

        // Stations filtered by lineId (200) and search (200).
        var byLine = await client.GetAsync($"/api/v1/stations?lineId={seed.Line1Id}&operatorCode={seed.OperatorCode}");
        Assert.Equal(HttpStatusCode.OK, byLine.StatusCode);
        using (var stDoc = JsonDocument.Parse(await byLine.Content.ReadAsStringAsync()))
        {
            var stations = stDoc.RootElement.GetProperty("stations").EnumerateArray().ToArray();
            Assert.Equal(4, stations.Length);
            Assert.Equal(seed.S1Id, stations[0].GetProperty("id").GetGuid());
            Assert.Equal(seed.S4Id, stations[3].GetProperty("id").GetGuid());
            Assert.True(stations[2].GetProperty("isInterchange").GetBoolean());
        }

        var search = await client.GetAsync($"/api/v1/stations?search=Interchange&operatorCode={seed.OperatorCode}");
        Assert.Equal(HttpStatusCode.OK, search.StatusCode);
        using (var searchDoc = JsonDocument.Parse(await search.Content.ReadAsStringAsync()))
            Assert.Equal(seed.S3Id, Assert.Single(searchDoc.RootElement.GetProperty("stations").EnumerateArray()).GetProperty("id").GetGuid());

        // 404 cases: unknown lineId and unknown operatorCode.
        var unknownLine = await client.GetAsync($"/api/v1/stations?lineId={Guid.NewGuid()}&operatorCode={seed.OperatorCode}");
        await AssertProblemAsync(unknownLine, HttpStatusCode.NotFound, "LINE_NOT_FOUND");

        var unknownOp = await client.GetAsync($"/api/v1/lines?operatorCode=UNKNOWN-{Guid.NewGuid():N}");
        await AssertProblemAsync(unknownOp, HttpStatusCode.NotFound, "OPERATOR_NOT_FOUND");
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQL")]
    public async Task Public_journeys_finds_direct_reverse_and_transfer_routes_and_handles_400_404_422()
    {
        var seed = await SeedNetworkAsync();
        using var client = factory.CreateClient();

        // Direct forward on L1: S1 -> S4 (3 segments, 0 transfers, 540s).
        var forwardRes = await client.GetAsync(
            $"/api/v1/journeys?originStationId={seed.S1Id}&destinationStationId={seed.S4Id}&operatorCode={seed.OperatorCode}");
        Assert.Equal(HttpStatusCode.OK, forwardRes.StatusCode);
        using (var doc = JsonDocument.Parse(await forwardRes.Content.ReadAsStringAsync()))
        {
            var option = Assert.Single(doc.RootElement.GetProperty("options").EnumerateArray());
            Assert.Equal(3, option.GetProperty("segmentCount").GetInt32());
            Assert.Equal(0, option.GetProperty("transferCount").GetInt32());
            Assert.Equal(540, option.GetProperty("estimatedDurationSeconds").GetInt32());
        }

        // Reverse direction on L1 (synthesized from direction 0): S4 -> S1.
        var reverseRes = await client.GetAsync(
            $"/api/v1/journeys?originStationId={seed.S4Id}&destinationStationId={seed.S1Id}&operatorCode={seed.OperatorCode}");
        Assert.Equal(HttpStatusCode.OK, reverseRes.StatusCode);
        using (var doc = JsonDocument.Parse(await reverseRes.Content.ReadAsStringAsync()))
        {
            var option = Assert.Single(doc.RootElement.GetProperty("options").EnumerateArray());
            Assert.Equal(3, option.GetProperty("segmentCount").GetInt32());
            Assert.Equal(1, option.GetProperty("legs")[0].GetProperty("direction").GetInt16());
        }

        // Cross-line transfer at S3: S1 (L1) -> S3 -> S6 (L2) = 3 segments, 1 transfer, 840s.
        var transferRes = await client.GetAsync(
            $"/api/v1/journeys?originStationId={seed.S1Id}&destinationStationId={seed.S6Id}&operatorCode={seed.OperatorCode}");
        Assert.Equal(HttpStatusCode.OK, transferRes.StatusCode);
        using (var doc = JsonDocument.Parse(await transferRes.Content.ReadAsStringAsync()))
        {
            var option = Assert.Single(doc.RootElement.GetProperty("options").EnumerateArray());
            Assert.Equal(3, option.GetProperty("segmentCount").GetInt32());
            Assert.Equal(1, option.GetProperty("transferCount").GetInt32());
            Assert.Equal(840, option.GetProperty("estimatedDurationSeconds").GetInt32());
            Assert.Equal(2, option.GetProperty("legs").GetArrayLength());
            Assert.Equal(seed.S3Id, option.GetProperty("transfers")[0].GetProperty("stationId").GetGuid());
        }

        // 400 missing params & same station.
        var missing = await client.GetAsync($"/api/v1/journeys?originStationId={seed.S1Id}&operatorCode={seed.OperatorCode}");
        await AssertProblemAsync(missing, HttpStatusCode.BadRequest, "INVALID_JOURNEY_QUERY");

        var same = await client.GetAsync(
            $"/api/v1/journeys?originStationId={seed.S1Id}&destinationStationId={seed.S1Id}&operatorCode={seed.OperatorCode}");
        await AssertProblemAsync(same, HttpStatusCode.BadRequest, "SAME_STATION");

        // 404 unknown station.
        var unknown = await client.GetAsync(
            $"/api/v1/journeys?originStationId={seed.S1Id}&destinationStationId={Guid.NewGuid()}&operatorCode={seed.OperatorCode}");
        await AssertProblemAsync(unknown, HttpStatusCode.NotFound, "STATION_NOT_FOUND");

        // 422 path unavailable (destination is CLOSED / on SUSPENDED line).
        var unavailable = await client.GetAsync(
            $"/api/v1/journeys?originStationId={seed.S1Id}&destinationStationId={seed.ClosedStationId}&operatorCode={seed.OperatorCode}");
        await AssertProblemAsync(unavailable, HttpStatusCode.UnprocessableEntity, "PATH_UNAVAILABLE");
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQL")]
    public async Task Public_service_status_returns_active_incidents_published_notices_manual_eta_and_404()
    {
        var seed = await SeedNetworkAsync();
        using var client = factory.CreateClient();

        var res = await client.GetAsync($"/api/v1/service-status?operatorCode={seed.OperatorCode}");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using (var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync()))
        {
            Assert.Equal("DISRUPTED", doc.RootElement.GetProperty("overallStatus").GetString());
            Assert.True(doc.RootElement.GetProperty("etaAvailable").GetBoolean());

            // Only active incident is returned (CLOSED incident excluded).
            var incidents = doc.RootElement.GetProperty("activeIncidents").EnumerateArray().ToArray();
            var activeIncident = Assert.Single(incidents);
            Assert.Equal(seed.ActiveIncidentCode, activeIncident.GetProperty("code").GetString());

            // Only published public notice is returned (DRAFT and INTERNAL excluded).
            var notices = doc.RootElement.GetProperty("notices").EnumerateArray().ToArray();
            var notice = Assert.Single(notices);
            Assert.Equal(seed.PublishedNoticeCode, notice.GetProperty("code").GetString());

            // Manual ETA: upcoming is UPDATED, expired is NO_ETA_UPDATE with null timestamps (BR-052).
            var etas = doc.RootElement.GetProperty("etaUpdates").EnumerateArray().ToArray();
            Assert.Equal(2, etas.Length);
            var validEta = Assert.Single(etas, e => e.GetProperty("stationId").GetGuid() == seed.S1Id);
            Assert.Equal("UPDATED", validEta.GetProperty("etaStatus").GetString());
            Assert.Equal(JsonValueKind.String, validEta.GetProperty("expectedArrivalAt").ValueKind);

            var expiredEta = Assert.Single(etas, e => e.GetProperty("stationId").GetGuid() == seed.S2Id);
            Assert.Equal("NO_ETA_UPDATE", expiredEta.GetProperty("etaStatus").GetString());
            Assert.Equal(JsonValueKind.Null, expiredEta.GetProperty("expectedArrivalAt").ValueKind);
        }

        // Scoped by lineId (200) and unknown filters (404).
        var scoped = await client.GetAsync($"/api/v1/service-status?lineId={seed.Line1Id}&operatorCode={seed.OperatorCode}");
        Assert.Equal(HttpStatusCode.OK, scoped.StatusCode);

        var badLine = await client.GetAsync($"/api/v1/service-status?lineId={Guid.NewGuid()}&operatorCode={seed.OperatorCode}");
        await AssertProblemAsync(badLine, HttpStatusCode.NotFound, "LINE_NOT_FOUND");

        var badStation = await client.GetAsync($"/api/v1/service-status?stationId={Guid.NewGuid()}&operatorCode={seed.OperatorCode}");
        await AssertProblemAsync(badStation, HttpStatusCode.NotFound, "STATION_NOT_FOUND");

        // No sensitive credentials or connection strings in captured logs.
        var logs = string.Join(Environment.NewLine, factory.TestLogs.Messages);
        Assert.DoesNotContain("Password=", logs, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<SeededNetwork> SeedNetworkAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        var opId = Guid.NewGuid();
        var opCode = $"CAT-{suffix}";
        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);

        await using var scope = factory.Services.CreateAsyncScope();
        var opContext = scope.ServiceProvider.GetRequiredService<IOperatorContext>();
        opContext.CurrentOperatorId = opId;
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var user = new IdentityUser
        {
            Id = $"cat-user-{suffix}",
            UserName = $"cat-{suffix}@example.invalid",
            NormalizedUserName = $"CAT-{suffix}@EXAMPLE.INVALID",
            Email = $"cat-{suffix}@example.invalid",
            NormalizedEmail = $"CAT-{suffix}@EXAMPLE.INVALID",
            EmailConfirmed = true
        };
        var op = new Operator { Id = opId, Code = opCode, Name = $"Catalog Test {suffix}", Status = "ACTIVE" };
        db.AddRange(user, op);
        await db.SaveChangesAsync();

        var l1 = new Line { OperatorId = opId, Code = $"L1-{suffix}", Name = "Line One", Status = "ACTIVE" };
        var l2 = new Line { OperatorId = opId, Code = $"L2-{suffix}", Name = "Line Two", Status = "ACTIVE" };
        var l3 = new Line { OperatorId = opId, Code = $"L3-{suffix}", Name = "Suspended Line", Status = "SUSPENDED" };

        var s1 = new Station { OperatorId = opId, Code = $"S1-{suffix}", Name = "Alpha", Status = "ACTIVE" };
        var s2 = new Station { OperatorId = opId, Code = $"S2-{suffix}", Name = "Beta", Status = "ACTIVE" };
        var s3 = new Station { OperatorId = opId, Code = $"S3-{suffix}", Name = "Central Interchange", StationType = "INTERCHANGE", MinTransferSeconds = 300, Status = "ACTIVE" };
        var s4 = new Station { OperatorId = opId, Code = $"S4-{suffix}", Name = "Delta", Status = "ACTIVE" };
        var s5 = new Station { OperatorId = opId, Code = $"S5-{suffix}", Name = "North", Status = "ACTIVE" };
        var s6 = new Station { OperatorId = opId, Code = $"S6-{suffix}", Name = "South", Status = "ACTIVE" };
        var s7 = new Station { OperatorId = opId, Code = $"S7-{suffix}", Name = "Closed Stop", Status = "CLOSED" };

        var cal = new ServiceCalendar { OperatorId = opId, Code = $"CAL-{suffix}", Name = "Daily", DayType = "WEEKDAY", DaysOfWeek = [1, 2, 3, 4, 5], EffectiveFrom = today, Status = "PUBLISHED", CreatedByUserId = user.Id };
        db.AddRange(l1, l2, l3, s1, s2, s3, s4, s5, s6, s7, cal);
        await db.SaveChangesAsync();

        var schedule = new ScheduleVersion { OperatorId = opId, LineId = l1.Id, ServiceCalendarId = cal.Id, Version = 1, Code = $"SCH-{suffix}", Status = "PUBLISHED", EffectiveFrom = now.AddDays(-1), CreatedByUserId = user.Id };
        db.AddRange(
            schedule,
            new LineStation { OperatorId = opId, LineId = l1.Id, StationId = s1.Id, Direction = 0, SequenceNo = 1, TravelTimeSeconds = 180, DistanceFromOriginM = 0 },
            new LineStation { OperatorId = opId, LineId = l1.Id, StationId = s2.Id, Direction = 0, SequenceNo = 2, TravelTimeSeconds = 180, DistanceFromOriginM = 1500 },
            new LineStation { OperatorId = opId, LineId = l1.Id, StationId = s3.Id, Direction = 0, SequenceNo = 3, TravelTimeSeconds = 180, DistanceFromOriginM = 3000 },
            new LineStation { OperatorId = opId, LineId = l1.Id, StationId = s4.Id, Direction = 0, SequenceNo = 4, TravelTimeSeconds = 180, DistanceFromOriginM = 4500 },
            new LineStation { OperatorId = opId, LineId = l2.Id, StationId = s5.Id, Direction = 0, SequenceNo = 1, TravelTimeSeconds = 180, DistanceFromOriginM = 0 },
            new LineStation { OperatorId = opId, LineId = l2.Id, StationId = s3.Id, Direction = 0, SequenceNo = 2, TravelTimeSeconds = 180, DistanceFromOriginM = 1200 },
            new LineStation { OperatorId = opId, LineId = l2.Id, StationId = s6.Id, Direction = 0, SequenceNo = 3, TravelTimeSeconds = 180, DistanceFromOriginM = 2400 },
            new LineStation { OperatorId = opId, LineId = l3.Id, StationId = s7.Id, Direction = 0, SequenceNo = 1, TravelTimeSeconds = 180, DistanceFromOriginM = 0 });
        await db.SaveChangesAsync();

        var trip = new PlannedTrip { OperatorId = opId, ScheduleVersionId = schedule.Id, LineId = l1.Id, TripCode = $"TRIP-{suffix}", ServiceDate = today, Direction = 0, PlannedStartAt = now.AddMinutes(-5), PlannedEndAt = now.AddHours(1), Status = "IN_SERVICE" };
        db.Add(trip);
        await db.SaveChangesAsync();

        var activeIncidentId = Guid.NewGuid();
        var activeIncidentCode = $"INC-ACT-{suffix}";
        var closedIncidentCode = $"INC-CLS-{suffix}";
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO metro.incidents (id, operator_id, code, incident_type, severity, status, line_id, station_id, occurred_at, summary, impact_summary, reported_by_user_id)
            VALUES ({activeIncidentId}, {opId}, {activeIncidentCode}, 'SIGNAL_DELAY', 'HIGH', 'IN_RESPONSE', {l1.Id}, {s3.Id}, {now.AddMinutes(-15)}, 'Signal delay at Central', '5 minute headway delay', {user.Id})
            """);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO metro.incidents (id, operator_id, code, incident_type, severity, status, line_id, station_id, occurred_at, recovered_at, closed_at, summary, recovery_summary, reported_by_user_id)
            VALUES ({Guid.NewGuid()}, {opId}, {closedIncidentCode}, 'DOOR_FAULT', 'LOW', 'CLOSED', {l2.Id}, {s5.Id}, {now.AddHours(-2)}, {now.AddHours(-1)}, {now.AddMinutes(-30)}, 'Resolved door issue', 'Door sensor reset', {user.Id})
            """);

        var publishedNoticeCode = $"NTC-PUB-{suffix}";
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO metro.service_notices (id, operator_id, incident_id, code, audience, title, content, status, emergency_template, valid_from, valid_to, created_by_user_id, published_at)
            VALUES ({Guid.NewGuid()}, {opId}, {activeIncidentId}, {publishedNoticeCode}, 'PUBLIC', 'Minor delay on Line One', 'Trains delayed 5 minutes.', 'PUBLISHED', true, {now.AddMinutes(-20)}, {now.AddHours(2)}, {user.Id}, {now.AddMinutes(-15)}),
                   ({Guid.NewGuid()}, {opId}, NULL, {$"NTC-DRF-{suffix}"}, 'PUBLIC', 'Draft notice', 'Not published yet.', 'DRAFT', false, {now.AddMinutes(-10)}, {now.AddHours(2)}, {user.Id}, NULL),
                   ({Guid.NewGuid()}, {opId}, NULL, {$"NTC-INT-{suffix}"}, 'INTERNAL', 'Internal memo', 'Staff only.', 'PUBLISHED', false, {now.AddMinutes(-10)}, {now.AddHours(2)}, {user.Id}, {now.AddMinutes(-5)})
            """);

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO metro.trip_eta_updates (id, operator_id, planned_trip_id, station_id, expected_arrival_at, expected_departure_at, source, recorded_by_user_id, recorded_at, note)
            VALUES ({Guid.NewGuid()}, {opId}, {trip.Id}, {s1.Id}, {now.AddMinutes(5)}, {now.AddMinutes(6)}, 'MANUAL', {user.Id}, {now.AddMinutes(-1)}, 'Approaching platform 1'),
                   ({Guid.NewGuid()}, {opId}, {trip.Id}, {s2.Id}, {now.AddMinutes(-10)}, {now.AddMinutes(-9)}, 'MANUAL', {user.Id}, {now.AddMinutes(-15)}, 'Expired ETA entry')
            """);

        return new SeededNetwork(opCode, l1.Id, l1.Code, s1.Id, s2.Id, s3.Id, s4.Id, s6.Id, s7.Id, activeIncidentCode, publishedNoticeCode);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, problem.RootElement.GetProperty("code").GetString());
    }

    private sealed record SeededNetwork(
        string OperatorCode,
        Guid Line1Id,
        string Line1Code,
        Guid S1Id,
        Guid S2Id,
        Guid S3Id,
        Guid S4Id,
        Guid S6Id,
        Guid ClosedStationId,
        string ActiveIncidentCode,
        string PublishedNoticeCode);
}
