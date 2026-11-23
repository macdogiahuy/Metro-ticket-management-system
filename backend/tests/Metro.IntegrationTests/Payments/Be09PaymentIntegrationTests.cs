using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Metro.Application.Abstractions.MultiTenancy;
using Metro.Infrastructure.Persistence.Context;
using Metro.Infrastructure.Ticketing;
using Metro.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Metro.IntegrationTests.Payments.PaymentScenarioFixture;
namespace Metro.IntegrationTests.Payments;

[Collection(PaymentCollection.Name)]
public sealed class Be09PaymentIntegrationTests(PaymentTestFactory factory)
{
    [PostgreSqlFact, Trait("Category", "PostgreSQL")]
    public async Task Valid_confirmed_order_webhook_worker_and_QR_use_one_ticket()
    {
        using var client = factory.CreateClient();
        var seed = await SeedAsync(factory, membership: true);
        client.DefaultRequestHeaders.Authorization = Bearer(seed.User);
        var created = await client.PostAsJsonAsync("/api/v1/payments", new { orderId = seed.Order, idempotencyKey = Guid.NewGuid().ToString() });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var payment = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var body = JsonSerializer.Serialize(new { eventId = Guid.NewGuid().ToString(), idempotencyKey = Guid.NewGuid().ToString(), paymentId = payment.ToString(), amount = 15000, currency = "VND" });
        for (var i = 0; i < 2; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/momo") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            request.Headers.Add("X-MoMo-Signature", Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(StubPaymentGateway.Secret), Encoding.UTF8.GetBytes(body))));
            var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(1, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("issuedTickets").GetInt32());
        }
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IOperatorContext>().CurrentOperatorId = seed.Op;
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal("PAID", (await db.Orders.AsNoTracking().SingleAsync(o => o.Id == seed.Order)).Status);
        await scope.ServiceProvider.GetRequiredService<TicketIssuanceProcessor>().ProcessDueAsync(500);
        var ticket = Assert.Single(await db.Tickets.AsNoTracking().Where(t => t.OrderId == seed.Order).ToListAsync());
        Assert.Equal("ISSUED", (await db.Orders.AsNoTracking().SingleAsync(o => o.Id == seed.Order)).Status);
        var qr = await client.GetAsync($"/api/v1/tickets/{ticket.Id}/qr?operatorCode={seed.Code}");
        Assert.Equal(HttpStatusCode.OK, qr.StatusCode);
        Assert.Equal("image/png", qr.Content.Headers.ContentType?.MediaType);
    }

}
