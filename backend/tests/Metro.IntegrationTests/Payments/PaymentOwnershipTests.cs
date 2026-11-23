using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Metro.Application.Abstractions.MultiTenancy;
using Metro.Infrastructure.Persistence.Context;
using Metro.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Metro.IntegrationTests.Payments.PaymentScenarioFixture;
using Metro.Domain.Entities.Identity;
namespace Metro.IntegrationTests.Payments;

[Collection(PaymentCollection.Name)]
public sealed class PaymentOwnershipTests(PaymentTestFactory factory)
{
    [PostgreSqlFact, Trait("Category", "PostgreSQL")]
    public async Task Passenger_with_valid_order_does_not_require_staff_operator_membership()
    {
        using var client = factory.CreateClient();
        var seed = await SeedAsync(factory, membership: false);
        client.DefaultRequestHeaders.Authorization = Bearer(seed.User);
        var result = await client.PostAsJsonAsync("/api/v1/payments", new { orderId = seed.Order, idempotencyKey = Guid.NewGuid().ToString() });
        Assert.Equal(HttpStatusCode.Created, result.StatusCode);
    }

    [PostgreSqlFact, Trait("Category", "PostgreSQL")]
    public async Task Another_account_in_same_operator_cannot_read_buyers_payment()
    {
        using var client = factory.CreateClient();
        var seed = await SeedAsync(factory, membership: true);
        client.DefaultRequestHeaders.Authorization = Bearer(seed.User);
        var created = await client.PostAsJsonAsync("/api/v1/payments", new { orderId = seed.Order, idempotencyKey = Guid.NewGuid().ToString() });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var payment = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await using var scope = factory.Services.CreateAsyncScope();
        var other = await UserAsync(scope.ServiceProvider);
        scope.ServiceProvider.GetRequiredService<IOperatorContext>().CurrentOperatorId = seed.Op;
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.OperatorAccounts.Add(new OperatorAccount { Id = Guid.NewGuid(), OperatorId = seed.Op, UserId = other, Status = "ACTIVE" });
        await db.SaveChangesAsync();
        client.DefaultRequestHeaders.Authorization = Bearer(other);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/payments/{payment}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/v1/payments", new { orderId = seed.Order, idempotencyKey = Guid.NewGuid().ToString() })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/api/v1/payments/{payment}/retry", null)).StatusCode);
    }

}
