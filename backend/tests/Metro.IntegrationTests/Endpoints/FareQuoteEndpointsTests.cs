using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Metro.Application.Abstractions.MultiTenancy;
using Metro.Domain.Entities.Identity;
using Metro.Domain.Entities.Network;
using Metro.Domain.Entities.Orders;
using Metro.Infrastructure.Persistence.Context;
using Metro.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Metro.IntegrationTests.Endpoints;

[Collection(DatabaseIntegrationCollection.Name)]
public sealed class FareQuoteEndpointsTests(MetroWebApplicationFactory factory)
{
    [PostgreSqlFact]
    [Trait("Category", "PostgreSQL")]
    public async Task Public_fare_quote_computes_by_published_version_segment_formula_od_override_and_errors()
    {
        var seed = await SeedFareCatalogAsync();
        using var client = factory.CreateClient();

        // 1. Single-trip 3 segments (S1->S4) via NULL-scope formula: 5,000 + 2*1,000 = 7,000 VND.
        var singleRes = await client.GetAsync(
            $"/api/v1/fares/quote?productCode={seed.SingleCode}&originStationId={seed.S1Id}&destinationStationId={seed.S4Id}&operatorCode={seed.OperatorCode}");
        Assert.Equal(HttpStatusCode.OK, singleRes.StatusCode);
        using (var doc = JsonDocument.Parse(await singleRes.Content.ReadAsStringAsync()))
        {
            var r = doc.RootElement;
            Assert.True(r.GetProperty("isReferenceQuote").GetBoolean());
            Assert.Equal(seed.FareVersionId, r.GetProperty("farePolicy").GetProperty("id").GetGuid());
            Assert.Equal(7_000, r.GetProperty("baseAmountMinor").GetInt64());
            Assert.Equal(0, r.GetProperty("discountAmountMinor").GetInt64());
            Assert.Equal(7_000, r.GetProperty("totalAmountMinor").GetInt64());
            Assert.Equal("SEGMENT_FORMULA", r.GetProperty("pricingSource").GetString());
            Assert.Equal(3, r.GetProperty("journey").GetProperty("segmentCount").GetInt32());
        }

        // 2. Explicit OD override (S1->S2) takes precedence over formula (9,000 VND).
        var odRes = await client.GetAsync(
            $"/api/v1/fares/quote?productCode={seed.SingleCode}&originStationId={seed.S1Id}&destinationStationId={seed.S2Id}&operatorCode={seed.OperatorCode}");
        Assert.Equal(HttpStatusCode.OK, odRes.StatusCode);
        using (var doc = JsonDocument.Parse(await odRes.Content.ReadAsStringAsync()))
        {
            Assert.Equal(9_000, doc.RootElement.GetProperty("totalAmountMinor").GetInt64());
            Assert.Equal("FARE_MATRIX_OD", doc.RootElement.GetProperty("pricingSource").GetString());
        }

        // 3. Periodic pass (DAY) + Guest STUDENT reference quote -> 30,000 - 10% = 27,000 VND, verificationRequired=true.
        var dayRes = await client.GetAsync(
            $"/api/v1/fares/quote?productCode={seed.DayCode}&eligibilityGroup=STUDENT&operatorCode={seed.OperatorCode}");
        Assert.Equal(HttpStatusCode.OK, dayRes.StatusCode);
        using (var doc = JsonDocument.Parse(await dayRes.Content.ReadAsStringAsync()))
        {
            var r = doc.RootElement;
            Assert.Equal(30_000, r.GetProperty("baseAmountMinor").GetInt64());
            Assert.Equal(3_000, r.GetProperty("discountAmountMinor").GetInt64());
            Assert.Equal(27_000, r.GetProperty("totalAmountMinor").GetInt64());
            Assert.True(r.GetProperty("verificationRequired").GetBoolean());
        }

        // 4. Error paths: missing journey (400), same station (400), unknown product (404), closed path (422), child on DAY (422).
        await AssertProblemAsync(
            await client.GetAsync($"/api/v1/fares/quote?productCode={seed.SingleCode}&operatorCode={seed.OperatorCode}"),
            HttpStatusCode.BadRequest, "JOURNEY_REQUIRED_FOR_SINGLE_TRIP");
        await AssertProblemAsync(
            await client.GetAsync($"/api/v1/fares/quote?productCode={seed.SingleCode}&originStationId={seed.S1Id}&destinationStationId={seed.S1Id}&operatorCode={seed.OperatorCode}"),
            HttpStatusCode.BadRequest, "SAME_STATION");
        await AssertProblemAsync(
            await client.GetAsync($"/api/v1/fares/quote?productCode=MISSING-{Guid.NewGuid():N}&operatorCode={seed.OperatorCode}"),
            HttpStatusCode.NotFound, "PRODUCT_NOT_FOUND");
        await AssertProblemAsync(
            await client.GetAsync($"/api/v1/fares/quote?productCode={seed.SingleCode}&originStationId={seed.S1Id}&destinationStationId={seed.ClosedStationId}&operatorCode={seed.OperatorCode}"),
            HttpStatusCode.UnprocessableEntity, "PATH_UNAVAILABLE");
        await AssertProblemAsync(
            await client.GetAsync($"/api/v1/fares/quote?productCode={seed.DayCode}&eligibilityGroup=CHILD_UNDER_6&operatorCode={seed.OperatorCode}"),
            HttpStatusCode.UnprocessableEntity, "CHILD_SINGLE_TRIP_ONLY");
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQL")]
    public async Task Authenticated_quote_checks_verified_elderly_age_fallback_and_child_free_without_logging_pii()
    {
        var seed = await SeedFareCatalogAsync();
        using var client = factory.CreateClient();

        // Register & verify an elderly passenger (>= 60 years old) and grant APPROVED elderly eligibility.
        var elderlyEmail = $"elderly-{Guid.NewGuid():N}@example.invalid";
        var elderlyPw = $"Metro!{Guid.NewGuid():N}Aa1";
        var regRes = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email = elderlyEmail, password = elderlyPw, fullName = "Nguyen Van Cao Tuoi", birthDate = "1960-01-01"
        });
        Assert.Equal(HttpStatusCode.Created, regRes.StatusCode);
        using var regDoc = JsonDocument.Parse(await regRes.Content.ReadAsStringAsync());
        var elderlyUserId = regDoc.RootElement.GetProperty("userId").GetString()!;
        var verifyCode = regDoc.RootElement.GetProperty("devVerificationCode").GetString()!;
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PostAsJsonAsync("/api/v1/auth/verify-email", new { email = elderlyEmail, code = verifyCode })).StatusCode);

        var loginRes = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = elderlyEmail, password = elderlyPw });
        Assert.Equal(HttpStatusCode.OK, loginRes.StatusCode);
        using var loginDoc = JsonDocument.Parse(await loginRes.Content.ReadAsStringAsync());
        var token = loginDoc.RootElement.GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Before approval: authenticated ELDERLY request falls back to ORDINARY (30,000 VND, BR-019).
        var unverified = await client.GetAsync(
            $"/api/v1/fares/quote?productCode={seed.DayCode}&eligibilityGroup=ELDERLY&operatorCode={seed.OperatorCode}");
        Assert.Equal(HttpStatusCode.OK, unverified.StatusCode);
        using (var doc = JsonDocument.Parse(await unverified.Content.ReadAsStringAsync()))
        {
            Assert.Equal("ORDINARY", doc.RootElement.GetProperty("appliedEligibilityGroup").GetString());
            Assert.True(doc.RootElement.GetProperty("fallbackToOrdinary").GetBoolean());
            Assert.Equal(30_000, doc.RootElement.GetProperty("totalAmountMinor").GetInt64());
        }

        // Seed APPROVED eligibility application for elderly passenger -> now receives 20% off (24,000 VND, AC-X04).
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<IOperatorContext>().CurrentOperatorId = seed.OperatorId;
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.EligibilityApplications.Add(new EligibilityApplication
            {
                OperatorId = seed.OperatorId, ApplicationCode = $"APP-{Guid.NewGuid():N}",
                ApplicantUserId = elderlyUserId, EligibilityPolicyId = seed.ElderlyPolicyId,
                Status = "APPROVED", ReviewedByUserId = seed.ApproverUserId, DecidedAt = DateTime.UtcNow,
                ValidFrom = new DateOnly(2026, 1, 1), ValidTo = new DateOnly(2028, 12, 31),
                DecisionReason = "Verified demo senior ID"
            });
            await db.SaveChangesAsync();
        }

        var verified = await client.GetAsync(
            $"/api/v1/fares/quote?productCode={seed.DayCode}&eligibilityGroup=ELDERLY&operatorCode={seed.OperatorCode}");
        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
        using (var doc = JsonDocument.Parse(await verified.Content.ReadAsStringAsync()))
        {
            Assert.Equal("ELDERLY", doc.RootElement.GetProperty("appliedEligibilityGroup").GetString());
            Assert.False(doc.RootElement.GetProperty("fallbackToOrdinary").GetBoolean());
            Assert.False(doc.RootElement.GetProperty("verificationRequired").GetBoolean());
            Assert.Equal(24_000, doc.RootElement.GetProperty("totalAmountMinor").GetInt64());
        }

        // Adult asking for CHILD_UNDER_6 is rejected with 422 (never silently falls back to paid fare, BR-021).
        await AssertProblemAsync(
            await client.GetAsync($"/api/v1/fares/quote?productCode={seed.SingleCode}&originStationId={seed.S1Id}&destinationStationId={seed.S4Id}&eligibilityGroup=CHILD_UNDER_6&operatorCode={seed.OperatorCode}"),
            HttpStatusCode.UnprocessableEntity, "CHILD_FREE_INELIGIBLE");

        // No PII or secret in logs.
        var logs = string.Join(Environment.NewLine, factory.TestLogs.Messages);
        Assert.DoesNotContain(elderlyEmail, logs, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(elderlyPw, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(token, logs, StringComparison.Ordinal);
    }

    private async Task<SeededFareCatalog> SeedFareCatalogAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        var opId = Guid.NewGuid();
        var opCode = $"FQ-{suffix}";
        var now = DateTime.UtcNow;

        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IOperatorContext>().CurrentOperatorId = opId;
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var creator = new IdentityUser { Id = $"fq-c-{suffix}", UserName = $"fqc-{suffix}", NormalizedUserName = $"FQC-{suffix}" };
        var approver = new IdentityUser { Id = $"fq-a-{suffix}", UserName = $"fqa-{suffix}", NormalizedUserName = $"FQA-{suffix}" };
        db.AddRange(creator, approver, new Operator { Id = opId, Code = opCode, Name = $"Fare Quote Op {suffix}", Status = "ACTIVE" });
        await db.SaveChangesAsync();

        var l1 = new Line { OperatorId = opId, Code = $"L1-{suffix}", Name = "Line 1", Status = "ACTIVE" };
        var s1 = new Station { OperatorId = opId, Code = $"S1-{suffix}", Name = "One", Status = "ACTIVE" };
        var s2 = new Station { OperatorId = opId, Code = $"S2-{suffix}", Name = "Two", Status = "ACTIVE" };
        var s3 = new Station { OperatorId = opId, Code = $"S3-{suffix}", Name = "Three", Status = "ACTIVE" };
        var s4 = new Station { OperatorId = opId, Code = $"S4-{suffix}", Name = "Four", Status = "ACTIVE" };
        var sClosed = new Station { OperatorId = opId, Code = $"SC-{suffix}", Name = "Closed", Status = "CLOSED" };

        var single = new TicketProduct { OperatorId = opId, Code = $"SINGLE-{suffix}", Name = "Single", ProductType = "SINGLE_TRIP", ExpiryRule = "SINGLE_JOURNEY", CreatedByUserId = creator.Id };
        var day = new TicketProduct { OperatorId = opId, Code = $"DAY-{suffix}", Name = "Day", ProductType = "DAY", DurationValue = 1, DurationUnit = "DAY", ExpiryRule = "NEXT_LOCAL_MIDNIGHT", CreatedByUserId = creator.Id };
        var version = new FarePolicyVersion
        {
            OperatorId = opId, Code = $"FV-{suffix}", Version = 1, ScopeKey = "DEFAULT", Status = "PUBLISHED",
            EffectiveFrom = now.AddDays(-1), CreatedByUserId = creator.Id, ApprovedByUserId = approver.Id,
            PublishedByUserId = approver.Id, ApprovedAt = now.AddDays(-1), PublishedAt = now.AddDays(-1)
        };
        var studentPolicy = new EligibilityPolicy
        {
            OperatorId = opId, Code = $"STU-{suffix}", Version = 1, BeneficiaryGroup = "STUDENT",
            DiscountType = "PERCENT", DiscountValue = 10m, EffectiveFrom = now.AddDays(-1), Status = "PUBLISHED",
            CreatedByUserId = creator.Id, ApprovedByUserId = approver.Id, ApprovedAt = now.AddDays(-1)
        };
        var elderlyPolicy = new EligibilityPolicy
        {
            OperatorId = opId, Code = $"ELD-{suffix}", Version = 1, BeneficiaryGroup = "ELDERLY",
            DiscountType = "PERCENT", DiscountValue = 20m, EffectiveFrom = now.AddDays(-1), Status = "PUBLISHED",
            CreatedByUserId = creator.Id, ApprovedByUserId = approver.Id, ApprovedAt = now.AddDays(-1)
        };
        db.AddRange(l1, s1, s2, s3, s4, sClosed, single, day, version, studentPolicy, elderlyPolicy);
        await db.SaveChangesAsync();

        db.AddRange(
            new LineStation { OperatorId = opId, LineId = l1.Id, StationId = s1.Id, Direction = 0, SequenceNo = 1, TravelTimeSeconds = 180, DistanceFromOriginM = 0 },
            new LineStation { OperatorId = opId, LineId = l1.Id, StationId = s2.Id, Direction = 0, SequenceNo = 2, TravelTimeSeconds = 180, DistanceFromOriginM = 1500 },
            new LineStation { OperatorId = opId, LineId = l1.Id, StationId = s3.Id, Direction = 0, SequenceNo = 3, TravelTimeSeconds = 180, DistanceFromOriginM = 3000 },
            new LineStation { OperatorId = opId, LineId = l1.Id, StationId = s4.Id, Direction = 0, SequenceNo = 4, TravelTimeSeconds = 180, DistanceFromOriginM = 4500 },
            new LineStation { OperatorId = opId, LineId = l1.Id, StationId = sClosed.Id, Direction = 0, SequenceNo = 5, TravelTimeSeconds = 180, DistanceFromOriginM = 6000 },
            new FareMatrixEntry { OperatorId = opId, FarePolicyVersionId = version.Id, TicketProductId = single.Id, LineId = null, OriginStationId = null, DestinationStationId = null, PaymentChannel = "ANY", EligibilityGroup = "ORDINARY", AmountMinor = 5_000, Sellable = true },
            new FareMatrixEntry { OperatorId = opId, FarePolicyVersionId = version.Id, TicketProductId = single.Id, LineId = l1.Id, OriginStationId = s1.Id, DestinationStationId = s2.Id, PaymentChannel = "ANY", EligibilityGroup = "ORDINARY", AmountMinor = 9_000, Sellable = true },
            new FareMatrixEntry { OperatorId = opId, FarePolicyVersionId = version.Id, TicketProductId = day.Id, LineId = null, OriginStationId = null, DestinationStationId = null, PaymentChannel = "ANY", EligibilityGroup = "ORDINARY", AmountMinor = 30_000, Sellable = true });
        await db.SaveChangesAsync();

        return new SeededFareCatalog(opId, opCode, version.Id, single.Code, day.Code, s1.Id, s2.Id, s4.Id, sClosed.Id, elderlyPolicy.Id, approver.Id);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, problem.RootElement.GetProperty("code").GetString());
    }

    private sealed record SeededFareCatalog(
        Guid OperatorId, string OperatorCode, Guid FareVersionId, string SingleCode, string DayCode,
        Guid S1Id, Guid S2Id, Guid S4Id, Guid ClosedStationId, Guid ElderlyPolicyId, string ApproverUserId);
}
