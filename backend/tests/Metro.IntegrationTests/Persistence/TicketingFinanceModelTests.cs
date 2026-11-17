using Metro.Domain.Entities.Finance;
using Metro.Domain.Common;
using Metro.Domain.Entities.LostProperty;
using Metro.Domain.Entities.Passengers;
using Metro.Domain.Entities.Tickets;
using Metro.Domain.Entities.Ticketing;
using Metro.Infrastructure.Persistence.Context;
using Metro.Infrastructure.Persistence.MultiTenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Metro.IntegrationTests.Persistence;

public class TicketingFinanceModelTests
{
    internal static readonly Type[] MappedTypes =
    [
        typeof(Payment), typeof(PaymentEvent), typeof(RefundRequest), typeof(RefundTransaction),
        typeof(RefundAttempt), typeof(RefundPayoutRevision), typeof(TicketCredential),
        typeof(Journey), typeof(AfcEvent), typeof(TripSuggestion), typeof(PurchaseConsent),
        typeof(GuardianLink), typeof(TicketChangeRequest), typeof(LostReport), typeof(FoundItem),
        typeof(LostItemClaim), typeof(LostItemHandover)
    ];

    internal static ApplicationDbContext CreateContext(string? connectionString = null, Guid? operatorId = null) => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString ?? "Host=localhost;Database=model_only;Username=unused")
            .ConfigureWarnings(w => w.Throw(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ShadowPropertyCreated))
            .Options,
        new OperatorContext { CurrentOperatorId = operatorId ?? Guid.Parse("a0000000-0000-0000-0000-000000000001") });

    [Fact]
    public void Model_keeps_domain_independent_and_maps_all_17_tables_without_legacy_capacity()
    {
        using var db = CreateContext();
        var entities = db.Model.GetEntityTypes()
            .Where(e => MappedTypes.Contains(e.ClrType)).ToArray();

        Assert.Equal(17, entities.Length);
        Assert.DoesNotContain(typeof(Payment).Assembly.GetReferencedAssemblies(),
            assembly => assembly.Name!.Contains("EntityFramework", StringComparison.Ordinal));
        Assert.All(entities, entity =>
        {
            Assert.Equal("metro", entity.GetSchema());
            Assert.All(entity.GetProperties(), property => Assert.False(property.IsShadowProperty()));
            Assert.All(entity.GetForeignKeys(), fk => Assert.Equal(DeleteBehavior.NoAction, fk.DeleteBehavior));
        });
        Assert.DoesNotContain(db.Model.GetEntityTypes(), entity =>
            entity.GetTableName()!.Contains("capacity", StringComparison.Ordinal));
        Assert.All(db.GetService<IDesignTimeModel>().Model.GetEntityTypes().Where(e => MappedTypes.Contains(e.ClrType)),
            entity => Assert.True(entity.IsTableExcludedFromMigrations()));
    }

    [Fact]
    public void Composite_foreign_keys_preserve_operator_and_order_version_boundaries()
    {
        using var db = CreateContext();
        var payment = db.Model.FindEntityType(typeof(Payment))!;
        var order = Assert.Single(payment.GetForeignKeys());
        Assert.Equal(new[] { "OperatorId", "OrderId" }, order.Properties.Select(p => p.Name));
        Assert.Equal(new[] { "OperatorId", "Id" }, order.PrincipalKey.Properties.Select(p => p.Name));

        foreach (var type in new[] { typeof(PurchaseConsent), typeof(TripSuggestion) })
        {
            var fk = Assert.Single(db.Model.FindEntityType(type)!.GetForeignKeys(),
                fk => fk.PrincipalEntityType.GetTableName() == "order_versions");
            Assert.Equal(new[] { "OperatorId", "OrderId", "OrderVersion" }, fk.Properties.Select(p => p.Name));
            Assert.Equal(new[] { "OperatorId", "OrderId", "Version" }, fk.PrincipalKey.Properties.Select(p => p.Name));
        }
    }

    [Theory]
    [InlineData(typeof(Payment), "uq_succeeded_payment_per_order", "'SUCCEEDED'")]
    [InlineData(typeof(RefundRequest), "uq_active_refund_per_ticket", "'PROCESSING'")]
    [InlineData(typeof(RefundTransaction), "uq_unresolved_refund_per_request", "'UNKNOWN'")]
    [InlineData(typeof(Journey), "uq_ticket_open_journey", "'OPEN'")]
    [InlineData(typeof(TripSuggestion), "uq_trip_suggestions_current", "superseded_at IS NULL")]
    [InlineData(typeof(PurchaseConsent), "uq_purchase_consents_pending", "'REQUESTED'")]
    [InlineData(typeof(TicketChangeRequest), "uq_ticket_change_applied", "'APPLIED'")]
    public void Critical_partial_unique_indexes_are_preserved(Type entityType, string indexName, string predicate)
    {
        using var db = CreateContext();
        var index = Assert.Single(db.Model.FindEntityType(entityType)!.GetIndexes(), i => i.GetDatabaseName() == indexName);
        Assert.True(index.IsUnique);
        Assert.Contains(predicate, index.GetFilter());
    }

    [Fact]
    public void Every_enum_value_roundtrips_to_upper_snake_case_text()
    {
        using var db = CreateContext();
        foreach (var entity in db.Model.GetEntityTypes().Where(e => MappedTypes.Contains(e.ClrType)))
        foreach (var property in entity.GetProperties().Where(p => p.ClrType.IsEnum))
        {
            var converter = property.GetTypeMapping().Converter!;
            Assert.NotNull(converter);
            foreach (var value in Enum.GetValues(property.ClrType))
            {
                var stored = Assert.IsType<string>(converter.ConvertToProvider(value));
                Assert.Matches("^[A-Z][A-Z0-9_]*$", stored);
                Assert.Equal(value, converter.ConvertFromProvider(stored));
            }
        }
        var statusConverter = db.Model.FindEntityType(typeof(Payment))!.FindProperty(nameof(Payment.Status))!.GetTypeMapping().Converter!;
        Assert.Equal("PARTIALLY_REFUNDED", statusConverter.ConvertToProvider(PaymentStatus.PartiallyRefunded));
    }

    [Fact]
    public void Integrated_model_uses_one_entity_per_core_table_and_filters_all_extensions()
    {
        using var db = CreateContext();
        var model = db.Model;
        foreach (var type in MappedTypes)
        {
            var entity = model.FindEntityType(type)!;
            Assert.True(typeof(IOperatorScoped).IsAssignableFrom(type));
            Assert.NotNull(entity.GetQueryFilter());
            Assert.True(entity.FindProperty("OperatorId")!.IsConcurrencyToken);
        }

        Assert.All(model.GetEntityTypes().Where(e => e.GetSchema() == "metro")
            .GroupBy(e => e.GetTableName()), group => Assert.Single(group));
        Assert.Equal(typeof(Metro.Domain.Entities.Orders.Order),
            Assert.Single(model.FindEntityType(typeof(Payment))!.GetForeignKeys()).PrincipalEntityType.ClrType);
        Assert.Equal(typeof(TicketCredential),
            model.FindEntityType(typeof(AfcEvent))!.FindNavigation(nameof(AfcEvent.Credential))!.TargetEntityType.ClrType);
    }

    [Fact]
    public async Task Integrated_payments_isolate_operator_reads_and_reject_foreign_writes()
    {
        var database = Guid.NewGuid().ToString();
        var first = new OperatorContext { CurrentOperatorId = Guid.NewGuid() };
        var second = new OperatorContext { CurrentOperatorId = Guid.NewGuid() };
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(database).Options;
        using var a = new ApplicationDbContext(options, first);
        using var b = new ApplicationDbContext(options, second);
        a.Payments.Add(new Payment { OperatorId = first.CurrentOperatorId!.Value, PaymentCode = "A", Provider = "TEST", IdempotencyKey = "A" });
        b.Payments.Add(new Payment { OperatorId = second.CurrentOperatorId!.Value, PaymentCode = "B", Provider = "TEST", IdempotencyKey = "B" });
        await a.SaveChangesAsync();
        await b.SaveChangesAsync();
        Assert.Equal("A", (await a.Payments.SingleAsync()).PaymentCode);
        Assert.Equal("B", (await b.Payments.SingleAsync()).PaymentCode);
        using var missing = new ApplicationDbContext(options);
        Assert.Empty(await missing.Payments.ToListAsync());
        a.Payments.Add(new Payment { OperatorId = second.CurrentOperatorId.Value, PaymentCode = "FOREIGN" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => a.SaveChangesAsync());
    }
}
