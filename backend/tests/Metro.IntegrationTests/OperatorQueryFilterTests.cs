using Metro.Application.Abstractions.MultiTenancy;
using Metro.Domain.Entities.Network;
using Metro.Domain.Entities.Orders;
using Metro.Domain.Entities.Tickets;
using Metro.Infrastructure.Persistence.Context;
using Metro.Infrastructure.Persistence.MultiTenancy;
using Microsoft.EntityFrameworkCore;

namespace Metro.IntegrationTests;

public class OperatorQueryFilterTests
{
    private static (ApplicationDbContext context, IOperatorContext operatorContext) CreateContextWithOperatorContext(string dbName)
    {
        var operatorContext = new OperatorContext();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;

        var context = new ApplicationDbContext(options, operatorContext);
        return (context, operatorContext);
    }

    [Fact]
    public async Task QueryFilter_Isolates_Data_By_OperatorId()
    {
        var dbName = Guid.NewGuid().ToString();
        var (context, opCtx) = CreateContextWithOperatorContext(dbName);

        var operatorA = Guid.NewGuid();
        var operatorB = Guid.NewGuid();

        // Seed data for both operators without filter active
        opCtx.CurrentOperatorId = null;

        var lineA = new Line
        {
            Id = Guid.NewGuid(),
            OperatorId = operatorA,
            Code = "L1",
            Name = "Line 1 (Operator A)"
        };

        var lineB = new Line
        {
            Id = Guid.NewGuid(),
            OperatorId = operatorB,
            Code = "L2",
            Name = "Line 2 (Operator B)"
        };

        var stationA = new Station
        {
            Id = Guid.NewGuid(),
            OperatorId = operatorA,
            Code = "ST-A",
            Name = "Station A"
        };

        var stationB = new Station
        {
            Id = Guid.NewGuid(),
            OperatorId = operatorB,
            Code = "ST-B",
            Name = "Station B"
        };

        context.Lines.AddRange(lineA, lineB);
        context.Stations.AddRange(stationA, stationB);
        await context.SaveChangesAsync();

        // 1. When CurrentOperatorId = operatorA, only operatorA entities are returned
        opCtx.CurrentOperatorId = operatorA;
        var linesForA = await context.Lines.ToListAsync();
        var stationsForA = await context.Stations.ToListAsync();

        Assert.Single(linesForA);
        Assert.Equal("L1", linesForA[0].Code);
        Assert.Equal(operatorA, linesForA[0].OperatorId);

        Assert.Single(stationsForA);
        Assert.Equal("ST-A", stationsForA[0].Code);
        Assert.Equal(operatorA, stationsForA[0].OperatorId);

        // 2. When CurrentOperatorId = operatorB, only operatorB entities are returned
        opCtx.CurrentOperatorId = operatorB;
        var linesForB = await context.Lines.ToListAsync();
        var stationsForB = await context.Stations.ToListAsync();

        Assert.Single(linesForB);
        Assert.Equal("L2", linesForB[0].Code);
        Assert.Equal(operatorB, linesForB[0].OperatorId);

        Assert.Single(stationsForB);
        Assert.Equal("ST-B", stationsForB[0].Code);
        Assert.Equal(operatorB, stationsForB[0].OperatorId);

        // 3. When CurrentOperatorId = null (system / admin), all entities are returned
        opCtx.CurrentOperatorId = null;
        var allLines = await context.Lines.ToListAsync();
        Assert.Equal(2, allLines.Count);

        // 4. When IgnoreQueryFilters() is called, all entities are returned even if operatorId is pinned
        opCtx.CurrentOperatorId = operatorA;
        var allLinesIgnored = await context.Lines.IgnoreQueryFilters().ToListAsync();
        Assert.Equal(2, allLinesIgnored.Count);
    }

    [Fact]
    public async Task QueryFilter_Applies_To_Orders_And_Tickets()
    {
        var dbName = Guid.NewGuid().ToString();
        var (context, opCtx) = CreateContextWithOperatorContext(dbName);

        var operatorA = Guid.NewGuid();
        var operatorB = Guid.NewGuid();

        opCtx.CurrentOperatorId = null;

        var orderA = new Order
        {
            Id = Guid.NewGuid(),
            OperatorId = operatorA,
            OrderCode = "ORD-A1",
            Channel = "WEB",
            FarePolicyVersionId = Guid.NewGuid()
        };

        var orderB = new Order
        {
            Id = Guid.NewGuid(),
            OperatorId = operatorB,
            OrderCode = "ORD-B1",
            Channel = "WEB",
            FarePolicyVersionId = Guid.NewGuid()
        };

        var ticketA = new Ticket
        {
            Id = Guid.NewGuid(),
            OperatorId = operatorA,
            TicketCode = "TCK-A1",
            OrderItemId = Guid.NewGuid(),
            TicketProductId = Guid.NewGuid(),
            FarePolicyVersionId = Guid.NewGuid(),
            SequenceNo = 1
        };

        var ticketB = new Ticket
        {
            Id = Guid.NewGuid(),
            OperatorId = operatorB,
            TicketCode = "TCK-B1",
            OrderItemId = Guid.NewGuid(),
            TicketProductId = Guid.NewGuid(),
            FarePolicyVersionId = Guid.NewGuid(),
            SequenceNo = 1
        };

        context.Orders.AddRange(orderA, orderB);
        context.Tickets.AddRange(ticketA, ticketB);
        await context.SaveChangesAsync();

        // Filter for Operator A
        opCtx.CurrentOperatorId = operatorA;
        var ordersForA = await context.Orders.ToListAsync();
        var ticketsForA = await context.Tickets.ToListAsync();

        Assert.Single(ordersForA);
        Assert.Equal("ORD-A1", ordersForA[0].OrderCode);

        Assert.Single(ticketsForA);
        Assert.Equal("TCK-A1", ticketsForA[0].TicketCode);

        // Filter for Operator B
        opCtx.CurrentOperatorId = operatorB;
        var ordersForB = await context.Orders.ToListAsync();
        var ticketsForB = await context.Tickets.ToListAsync();

        Assert.Single(ordersForB);
        Assert.Equal("ORD-B1", ordersForB[0].OrderCode);

        Assert.Single(ticketsForB);
        Assert.Equal("TCK-B1", ticketsForB[0].TicketCode);
    }
}
