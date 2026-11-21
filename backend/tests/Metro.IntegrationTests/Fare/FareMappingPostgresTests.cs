using Metro.Domain.Entities.Identity;
using Metro.Domain.Entities.Network;
using Metro.Domain.Entities.Orders;
using Metro.Infrastructure.Persistence.Context;
using Metro.Infrastructure.Persistence.MultiTenancy;
using Metro.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Metro.IntegrationTests.Fare;

public sealed class FareMappingPostgresTests
{
    private static ApplicationDbContext Context(NpgsqlConnection connection, Guid? operatorId) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connection).Options,
            new OperatorContext { CurrentOperatorId = operatorId });

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQL")]
    public async Task Fare_and_eligibility_mappings_enforce_null_safe_uniqueness_duration_and_operator_isolation()
    {
        var cs = PostgreSqlTestConfiguration.LoadRequired().ConnectionString;
        await using var connection = new NpgsqlConnection(cs);
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();

        var opA = Guid.NewGuid();
        var opB = Guid.NewGuid();
        await using var ca = Context(connection, opA);
        await using var cb = Context(connection, opB);
        await ca.Database.UseTransactionAsync(tx);
        await cb.Database.UseTransactionAsync(tx);

        var suffix = Guid.NewGuid().ToString("N")[..12];
        var now = new DateTime(2026, 11, 22, 0, 0, 0, DateTimeKind.Utc);
        var creator = new IdentityUser { Id = $"creator-{suffix}", UserName = $"c-{suffix}", NormalizedUserName = $"C-{suffix}".ToUpperInvariant() };
        var approver = new IdentityUser { Id = $"approver-{suffix}", UserName = $"a-{suffix}", NormalizedUserName = $"A-{suffix}".ToUpperInvariant() };
        var passenger = new IdentityUser { Id = $"pass-{suffix}", UserName = $"p-{suffix}", NormalizedUserName = $"P-{suffix}".ToUpperInvariant() };
        ca.AddRange(creator, approver, passenger,
            new Operator { Id = opA, Code = $"FA-{suffix}", Name = "Fare Op A" },
            new Operator { Id = opB, Code = $"FB-{suffix}", Name = "Fare Op B" });
        await ca.SaveChangesAsync();

        var line = new Line { OperatorId = opA, Code = $"L1-{suffix}", Name = "Line 1" };
        var s1 = new Station { OperatorId = opA, Code = $"S1-{suffix}", Name = "Origin" };
        var s2 = new Station { OperatorId = opA, Code = $"S2-{suffix}", Name = "Dest" };
        var dayProduct = new TicketProduct
        {
            OperatorId = opA, Code = $"DAY-{suffix}", Name = "Day pass", ProductType = "DAY",
            DurationValue = 1, DurationUnit = "DAY", ExpiryRule = "NEXT_LOCAL_MIDNIGHT", CreatedByUserId = creator.Id
        };
        var fareVersion = new FarePolicyVersion
        {
            OperatorId = opA, Code = $"FP-{suffix}", Version = 1, ScopeKey = $"SCOPE-{suffix}",
            Status = "PUBLISHED", EffectiveFrom = now, CreatedByUserId = creator.Id,
            ApprovedByUserId = approver.Id, PublishedByUserId = approver.Id, ApprovedAt = now, PublishedAt = now
        };
        var policy = new EligibilityPolicy
        {
            OperatorId = opA, Code = $"STU-{suffix}", Version = 1, BeneficiaryGroup = "STUDENT",
            DiscountType = "PERCENT", DiscountValue = 10m, EligibleProductCodes = [dayProduct.Code],
            RequiredEvidenceTypes = ["STUDENT_CARD"], EffectiveFrom = now, Status = "PUBLISHED",
            CreatedByUserId = creator.Id, ApprovedByUserId = approver.Id, ApprovedAt = now
        };
        ca.AddRange(line, s1, s2, dayProduct, fareVersion, policy);
        await ca.SaveChangesAsync();

        var nullScopeEntry = new FareMatrixEntry
        {
            OperatorId = opA, FarePolicyVersionId = fareVersion.Id, TicketProductId = dayProduct.Id,
            LineId = null, OriginStationId = null, DestinationStationId = null,
            PaymentChannel = "ANY", EligibilityGroup = "ORDINARY", AmountMinor = 30_000, Sellable = true
        };
        var app = new EligibilityApplication
        {
            OperatorId = opA, ApplicationCode = $"APP-{suffix}", ApplicantUserId = passenger.Id,
            EligibilityPolicyId = policy.Id, Status = "APPROVED", ReviewedByUserId = approver.Id,
            DecidedAt = now, ValidFrom = new DateOnly(2026, 11, 1), ValidTo = new DateOnly(2027, 11, 1),
            DecisionReason = "Valid student card"
        };
        ca.AddRange(nullScopeEntry, app);
        await ca.SaveChangesAsync();

        // Operator isolation: operator B sees zero rows from operator A.
        Assert.Empty(await cb.FareMatrixEntries.AsNoTracking().ToListAsync());
        Assert.Empty(await cb.EligibilityPolicies.AsNoTracking().ToListAsync());
        Assert.Empty(await cb.EligibilityApplications.AsNoTracking().ToListAsync());

        // Round-trip read on operator A succeeds.
        Assert.Equal(30_000, (await ca.FareMatrixEntries.AsNoTracking().SingleAsync(x => x.Id == nullScopeEntry.Id)).AmountMinor);
        Assert.Equal(10m, (await ca.EligibilityPolicies.AsNoTracking().SingleAsync(x => x.Id == policy.Id)).DiscountValue);
        Assert.Equal("APPROVED", (await ca.EligibilityApplications.AsNoTracking().SingleAsync(x => x.Id == app.Id)).Status);

        // NULL-safe unique constraint: duplicate NULL-scope fare entry raises SQLSTATE 23505.
        await tx.SaveAsync("null_safe_dup");
        ca.FareMatrixEntries.Add(new FareMatrixEntry
        {
            OperatorId = opA, FarePolicyVersionId = fareVersion.Id, TicketProductId = dayProduct.Id,
            LineId = null, OriginStationId = null, DestinationStationId = null,
            PaymentChannel = "ANY", EligibilityGroup = "ORDINARY", AmountMinor = 35_000, Sellable = true
        });
        var dupEx = await Assert.ThrowsAsync<DbUpdateException>(() => ca.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(dupEx.InnerException).SqlState);
        ca.ChangeTracker.Clear();
        await tx.RollbackAsync("null_safe_dup");

        // Non-SINGLE_TRIP product missing duration raises SQLSTATE 23514 CHECK violation.
        await tx.SaveAsync("missing_duration");
        ca.TicketProducts.Add(new TicketProduct
        {
            OperatorId = opA, Code = $"BAD-{suffix}", Name = "Missing duration",
            ProductType = "MONTH", DurationValue = null, DurationUnit = "MONTH",
            ExpiryRule = "ELAPSED_DURATION", CreatedByUserId = creator.Id
        });
        var checkEx = await Assert.ThrowsAsync<DbUpdateException>(() => ca.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, Assert.IsType<PostgresException>(checkEx.InnerException).SqlState);
        ca.ChangeTracker.Clear();
        await tx.RollbackAsync("missing_duration");
    }
}
