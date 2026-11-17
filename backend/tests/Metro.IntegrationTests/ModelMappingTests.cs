using Metro.Domain.Entities.Identity;
using Metro.Domain.Entities.Network;
using Metro.Domain.Entities.Orders;
using Metro.Domain.Entities.Tickets;
using Metro.Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace Metro.IntegrationTests;

public class ModelMappingTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=metro_model_test")
            .Options;

        return new ApplicationDbContext(options);
    }

    [Fact]
    public void Model_Contains_All_Core_Entities_In_Metro_Schema()
    {
        using var context = CreateContext();
        var model = context.Model;

        var expectedEntities = new Dictionary<Type, string>
        {
            // Identity
            { typeof(Operator), "operators" },
            { typeof(Bank), "banks" },
            { typeof(UserProfile), "user_profiles" },
            { typeof(AuthSession), "auth_sessions" },
            { typeof(EmailVerificationChallenge), "email_verification_challenges" },
            { typeof(OperatorAccount), "operator_accounts" },
            { typeof(BusinessRole), "business_roles" },
            { typeof(Permission), "permissions" },
            { typeof(RolePermission), "role_permissions" },
            { typeof(AccountRoleAssignment), "account_role_assignments" },
            { typeof(AuditLog), "audit_logs" },

            // Network
            { typeof(Line), "lines" },
            { typeof(Station), "stations" },
            { typeof(LineStation), "line_stations" },
            { typeof(ServiceCalendar), "service_calendars" },
            { typeof(ScheduleVersion), "schedule_versions" },
            { typeof(PlannedTrip), "planned_trips" },
            { typeof(PlannedStopTime), "planned_stop_times" },
            { typeof(ActualTrip), "actual_trips" },
            { typeof(ActualStopTime), "actual_stop_times" },

            // Orders & Pricing
            { typeof(TicketProduct), "ticket_products" },
            { typeof(FarePolicyVersion), "fare_policy_versions" },
            { typeof(Order), "orders" },
            { typeof(OrderItem), "order_items" },
            { typeof(OrderVersion), "order_versions" },

            // Tickets
            { typeof(Ticket), "tickets" },
            { typeof(TicketCredential), "ticket_credentials" }
        };

        foreach (var (entityType, tableName) in expectedEntities)
        {
            var efType = model.FindEntityType(entityType);
            Assert.NotNull(efType);
            Assert.Equal(tableName, efType.GetTableName());
            Assert.Equal("metro", efType.GetSchema());
        }
    }

    [Fact]
    public void Model_Does_Not_Map_Legacy_Capacity_Tables()
    {
        // DoD: "không map bảng capacity legacy"
        using var context = CreateContext();
        var model = context.Model;

        var tableNames = model.GetEntityTypes()
            .Select(t => t.GetTableName())
            .Where(t => t != null)
            .ToHashSet();

        Assert.DoesNotContain("capacity_profiles", tableNames);
        Assert.DoesNotContain("trip_capacity_segments", tableNames);
        Assert.DoesNotContain("capacity_holds", tableNames);
        Assert.DoesNotContain("capacity_hold_segments", tableNames);
    }

    [Fact]
    public void Npgsql_Model_Has_No_Shadow_Foreign_Keys_And_Maps_Changed_Columns()
    {
        using var context = CreateContext();
        Assert.DoesNotContain(context.Model.GetEntityTypes().SelectMany(e => e.GetForeignKeys())
            .SelectMany(f => f.Properties), p => p.IsShadowProperty());
        Assert.Equal(typeof(string), context.Model.FindEntityType(typeof(Microsoft.AspNetCore.Identity.IdentityUser))!
            .FindPrimaryKey()!.Properties.Single().ClrType);
        foreach (var entity in context.Model.GetEntityTypes().Where(e =>
                     typeof(Metro.Domain.Common.IOperatorScoped).IsAssignableFrom(e.ClrType) ||
                     typeof(Metro.Domain.Common.IOptionalOperatorScoped).IsAssignableFrom(e.ClrType)))
            Assert.True(entity.FindProperty("OperatorId")!.IsConcurrencyToken);
        var stop = context.Model.FindEntityType(typeof(ActualStopTime))!;
        Assert.Null(stop.FindProperty("DwellSeconds"));
        Assert.Null(stop.FindProperty("Irregularity"));
        Assert.False(stop.FindProperty(nameof(ActualStopTime.Source))!.IsNullable);
        Assert.Contains("source", context.ActualStopTimes.ToQueryString());
        Assert.DoesNotContain("dwell_seconds", context.ActualStopTimes.ToQueryString());
        Assert.Contains("order_version", context.Orders.ToQueryString());
        Assert.Contains("boarding_status", context.PlannedTrips.ToQueryString());
        var trip = context.Model.FindEntityType(typeof(PlannedTrip))!;
        Assert.True(trip.FindProperty(nameof(PlannedTrip.LoadProfileId))!.IsNullable);
        Assert.DoesNotContain(trip.GetNavigations(), n => n.TargetEntityType.GetTableName() == "capacity_profiles");
        Assert.Equal("OPEN", trip.FindProperty(nameof(PlannedTrip.BoardingStatus))!.GetDefaultValue());
        Assert.Equal(1, context.Model.FindEntityType(typeof(Order))!
            .FindProperty(nameof(Order.OrderVersion))!.GetDefaultValue());
        Assert.Equal(300, context.Model.FindEntityType(typeof(Station))!
            .FindProperty(nameof(Station.MinTransferSeconds))!.GetDefaultValue());
        Assert.Equal(180, context.Model.FindEntityType(typeof(LineStation))!
            .FindProperty(nameof(LineStation.TravelTimeSeconds))!.GetDefaultValue());
    }
}

