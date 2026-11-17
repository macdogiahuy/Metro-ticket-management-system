using System.Reflection;
using Metro.Application.Abstractions.MultiTenancy;
using Metro.Domain.Common;
using Metro.Domain.Entities.Identity;
using Metro.Domain.Entities.Network;
using Metro.Domain.Entities.Orders;
using Metro.Domain.Entities.Tickets;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Metro.Infrastructure.Persistence.Context;

public class ApplicationDbContext : IdentityDbContext<IdentityUser>
{
    private readonly IOperatorContext? _operatorContext;

    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options,
        IOperatorContext? operatorContext = null)
        : base(options)
    {
        _operatorContext = operatorContext;
    }

    /// <summary>
    /// Current operator ID used by multi-tenancy global query filter.
    /// Evaluated dynamically per query.
    /// </summary>
    public Guid? CurrentOperatorId => _operatorContext?.CurrentOperatorId;

    // Identity domain entities
    public DbSet<Operator> Operators => Set<Operator>();
    public DbSet<Bank> Banks => Set<Bank>();
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
    public DbSet<AuthSession> AuthSessions => Set<AuthSession>();
    public DbSet<EmailVerificationChallenge> EmailVerificationChallenges => Set<EmailVerificationChallenge>();
    public DbSet<OperatorAccount> OperatorAccounts => Set<OperatorAccount>();
    public DbSet<BusinessRole> BusinessRoles => Set<BusinessRole>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<AccountRoleAssignment> AccountRoleAssignments => Set<AccountRoleAssignment>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    // Network domain entities
    public DbSet<Line> Lines => Set<Line>();
    public DbSet<Station> Stations => Set<Station>();
    public DbSet<LineStation> LineStations => Set<LineStation>();
    public DbSet<ServiceCalendar> ServiceCalendars => Set<ServiceCalendar>();
    public DbSet<ScheduleVersion> ScheduleVersions => Set<ScheduleVersion>();
    public DbSet<PlannedTrip> PlannedTrips => Set<PlannedTrip>();
    public DbSet<PlannedStopTime> PlannedStopTimes => Set<PlannedStopTime>();
    public DbSet<ActualTrip> ActualTrips => Set<ActualTrip>();
    public DbSet<ActualStopTime> ActualStopTimes => Set<ActualStopTime>();

    // Order & Pricing domain entities
    public DbSet<TicketProduct> TicketProducts => Set<TicketProduct>();
    public DbSet<FarePolicyVersion> FarePolicyVersions => Set<FarePolicyVersion>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderVersion> OrderVersions => Set<OrderVersion>();

    // Ticket domain entities
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<TicketCredential> TicketCredentials => Set<TicketCredential>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ValidateOperatorWrites();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ValidateOperatorWrites();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ValidateOperatorWrites()
    {
        foreach (var entry in ChangeTracker.Entries().Where(e =>
                     e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            if (entry.Entity is not IOperatorScoped && entry.Entity is not IOptionalOperatorScoped) continue;
            var property = entry.Property("OperatorId");
            if (CurrentOperatorId is null || CurrentOperatorId == Guid.Empty ||
                !Equals(property.CurrentValue, CurrentOperatorId) ||
                (entry.State != EntityState.Added && !Equals(property.OriginalValue, CurrentOperatorId)))
                throw new InvalidOperationException("The current operator cannot write this scoped entity.");
        }
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Apply entity configurations from current assembly
        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        // Apply multi-tenancy query filter on all operator-scoped entities
        ApplyOperatorQueryFilters(builder);
    }

    private void ApplyOperatorQueryFilters(ModelBuilder builder)
    {
        var scopedMethod = typeof(ApplicationDbContext)
            .GetMethod(nameof(ConfigureOperatorFilter), BindingFlags.NonPublic | BindingFlags.Instance);
        var optionalScopedMethod = typeof(ApplicationDbContext)
            .GetMethod(nameof(ConfigureOptionalOperatorFilter), BindingFlags.NonPublic | BindingFlags.Instance);

        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            if (typeof(IOperatorScoped).IsAssignableFrom(entityType.ClrType))
            {
                var genericMethod = scopedMethod?.MakeGenericMethod(entityType.ClrType);
                genericMethod?.Invoke(this, new object[] { builder });
            }
            else if (typeof(IOptionalOperatorScoped).IsAssignableFrom(entityType.ClrType))
            {
                var genericMethod = optionalScopedMethod?.MakeGenericMethod(entityType.ClrType);
                genericMethod?.Invoke(this, new object[] { builder });
            }
        }
    }

    private void ConfigureOperatorFilter<TEntity>(ModelBuilder builder)
        where TEntity : class, IOperatorScoped
    {
        builder.Entity<TEntity>().Property(e => e.OperatorId).IsConcurrencyToken();
        builder.Entity<TEntity>().HasQueryFilter(e => CurrentOperatorId != null && CurrentOperatorId != Guid.Empty && e.OperatorId == CurrentOperatorId);
    }

    private void ConfigureOptionalOperatorFilter<TEntity>(ModelBuilder builder)
        where TEntity : class, IOptionalOperatorScoped
    {
        builder.Entity<TEntity>().Property(e => e.OperatorId).IsConcurrencyToken();
        builder.Entity<TEntity>().HasQueryFilter(e => CurrentOperatorId != null && CurrentOperatorId != Guid.Empty && e.OperatorId == CurrentOperatorId);
    }
}
