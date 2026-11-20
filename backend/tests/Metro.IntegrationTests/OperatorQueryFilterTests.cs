using Metro.Domain.Entities.Network;
using Metro.Domain.Entities.Identity;
using Metro.Infrastructure.Persistence.Context;
using Metro.Infrastructure.Persistence.MultiTenancy;
using Microsoft.EntityFrameworkCore;

namespace Metro.IntegrationTests;

public class OperatorQueryFilterTests
{
    private static ApplicationDbContext Context(string database, OperatorContext? op = null) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(database).Options, op);

    [Fact]
    public async Task Independent_Contexts_Isolate_Operators_And_Missing_Or_Unknown_Operator_Reads_Nothing()
    {
        var database = Guid.NewGuid().ToString();
        var a = new OperatorContext { CurrentOperatorId = Guid.NewGuid() };
        var b = new OperatorContext { CurrentOperatorId = Guid.NewGuid() };
        using var ca = Context(database, a);
        using var cb = Context(database, b);
        ca.Lines.Add(new Line { OperatorId = a.CurrentOperatorId!.Value, Code = "A", Name = "A" });
        cb.Lines.Add(new Line { OperatorId = b.CurrentOperatorId!.Value, Code = "B", Name = "B" });
        await ca.SaveChangesAsync();
        await cb.SaveChangesAsync();
        var results = await Task.WhenAll(ca.Lines.AsNoTracking().ToListAsync(), cb.Lines.AsNoTracking().ToListAsync());
        Assert.Equal("A", Assert.Single(results[0]).Code);
        Assert.Equal("B", Assert.Single(results[1]).Code);
        using var missing = Context(database);
        Assert.Empty(await missing.Lines.ToListAsync());
        foreach (var id in new Guid?[] { null, Guid.Empty, Guid.NewGuid() })
        {
            a.CurrentOperatorId = id;
            Assert.Empty(await ca.Lines.AsNoTracking().ToListAsync());
        }
        Assert.Equal("B", (await cb.Lines.SingleAsync()).Code);
        // Shared identity/reference data is not made operator-scoped.
        Assert.Null(ca.Model.FindEntityType(typeof(UserProfile))!.GetQueryFilter());
    }

    [Fact]
    public async Task Writes_Reject_Missing_Operator_And_An_Entire_Foreign_Graph()
    {
        var database = Guid.NewGuid().ToString();
        using var missing = Context(database);
        missing.Lines.Add(new Line { OperatorId = Guid.NewGuid() });
        Assert.Throws<InvalidOperationException>(() => missing.SaveChanges());
        await Assert.ThrowsAsync<InvalidOperationException>(() => missing.SaveChangesAsync());
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        using var ca = Context(database, new OperatorContext { CurrentOperatorId = a });
        ca.LineStations.Add(new LineStation {
            OperatorId = b, Line = new Line { OperatorId = b }, Station = new Station { OperatorId = b }
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => ca.SaveChangesAsync());
    }

    [Fact]
    public async Task Attached_Foreign_Update_Delete_And_Operator_Reassignment_Are_Rejected()
    {
        var op = new OperatorContext { CurrentOperatorId = Guid.NewGuid() };
        using var context = Context(Guid.NewGuid().ToString(), op);
        var line = new Line { OperatorId = op.CurrentOperatorId!.Value, Code = "A", Name = "A" };
        context.Add(line);
        context.SaveChanges();
        op.CurrentOperatorId = Guid.NewGuid();
        line.Name = "Changed";
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        context.Entry(line).State = EntityState.Deleted;
        Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
        context.Entry(line).State = EntityState.Unchanged;
        Assert.Throws<InvalidOperationException>(() =>
        {
            context.Entry(line).Property(x => x.OperatorId).CurrentValue = op.CurrentOperatorId.Value;
            context.SaveChanges();
        });
    }

    [Fact]
    public async Task Tenantless_profile_audit_can_be_added_but_not_updated_deleted_or_used_to_move_tenant_logs()
    {
        var database = Guid.NewGuid().ToString();
        using (var global = Context(database))
        {
            var audit = new AuditLog { Action = "PROFILE_UPDATED", EntityType = "user_profiles", EntityId = "owner" };
            global.AuditLogs.Add(audit);
            Assert.Equal(1, await global.SaveChangesAsync());
            audit.Reason = "Change history after insert";
            await Assert.ThrowsAsync<InvalidOperationException>(() => global.SaveChangesAsync());
            global.Entry(audit).State = EntityState.Unchanged;
            global.AuditLogs.Remove(audit);
            Assert.Throws<InvalidOperationException>(() => global.SaveChanges());
        }

        var ownerOperator = Guid.NewGuid();
        using var scoped = Context(database, new OperatorContext { CurrentOperatorId = ownerOperator });
        var ownAudit = new AuditLog { OperatorId = ownerOperator, Action = "TENANT_ACTION" };
        scoped.AuditLogs.Add(ownAudit);
        Assert.Equal(1, await scoped.SaveChangesAsync());
        ownAudit.OperatorId = null;
        await Assert.ThrowsAsync<InvalidOperationException>(() => scoped.SaveChangesAsync());
        scoped.ChangeTracker.Clear();
        scoped.AuditLogs.Add(new AuditLog { OperatorId = Guid.NewGuid(), Action = "FOREIGN_ACTION" });
        Assert.Throws<InvalidOperationException>(() => scoped.SaveChanges());
    }
}
