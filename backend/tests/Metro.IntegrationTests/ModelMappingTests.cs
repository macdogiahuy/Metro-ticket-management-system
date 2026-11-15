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
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
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
}
