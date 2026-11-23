using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Metro.Infrastructure.Persistence.Context;
using Metro.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Metro.IntegrationTests.Payments;

[Collection(PaymentCollection.Name)]
public sealed class PaymentEndpointsTests(PaymentTestFactory factory)
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQL")]
    public async Task Create_webhook_idempotently_issues_exactly_one_ticket_set()
    {
        using var client = factory.CreateClient();
        var (userId, operatorId, orderId) = await SeedAsync();
        client.DefaultRequestHeaders.Authorization = await BearerAsync(userId);

        var created = await client.PostAsJsonAsync("/api/v1/payments", new { orderId, idempotencyKey = "idem-1" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var paymentDoc = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var paymentId = paymentDoc.RootElement.GetProperty("id").GetGuid();

        var body = BuildWebhookBody(paymentId, "evt-1", "idem-1", 15000);
        var first = await PostWebhookAsync(client, body);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using var firstDoc = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        Assert.Equal(1, firstDoc.RootElement.GetProperty("issuedTickets").GetInt32());

        // Exact same callback again → SQL payment_events unique + ON CONFLICT make it a no-op.
        var second = await PostWebhookAsync(client, body);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        using var secondDoc = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        Assert.Equal(1, secondDoc.RootElement.GetProperty("issuedTickets").GetInt32());

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var orderItemId = await db.OrderItems.IgnoreQueryFilters()
            .Where(i => i.OrderId == orderId).Select(i => i.Id).SingleAsync();
        Assert.Equal(1, await db.Tickets.IgnoreQueryFilters().CountAsync(t => t.OrderItemId == orderItemId));
        Assert.Equal("SUCCEEDED", (await db.Payments.IgnoreQueryFilters().AsNoTracking().SingleAsync(p => p.Id == paymentId)).Status.ToString().ToUpperInvariant());
        Assert.Equal("ISSUED", (await db.Orders.IgnoreQueryFilters().AsNoTracking().SingleAsync(o => o.Id == orderId)).Status);
        Assert.Equal(1, await db.PaymentEvents.IgnoreQueryFilters().CountAsync(e => e.PaymentId == paymentId));
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQL")]
    public async Task Protected_endpoints_require_a_valid_token_and_active_operator()
    {
        using var client = factory.CreateClient();
        var (userId, _, _) = await SeedAsync();
        // An authenticated user with no operator_accounts membership.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var id = Guid.NewGuid().ToString("N");
            Assert.True((await users.CreateAsync(new IdentityUser
            {
                Id = id, UserName = id, Email = $"{id}@example.invalid", EmailConfirmed = true,
            })).Succeeded);
            client.DefaultRequestHeaders.Authorization = await BearerAsync(id);
        }

        var noMembership = await client.PostAsJsonAsync("/api/v1/payments", new { orderId = Guid.NewGuid(), idempotencyKey = "idem-x" });
        Assert.Equal(HttpStatusCode.Forbidden, noMembership.StatusCode);
        using var problem = JsonDocument.Parse(await noMembership.Content.ReadAsStringAsync());
        Assert.Equal("OPERATOR_NOT_FOUND", problem.RootElement.GetProperty("code").GetString());

        client.DefaultRequestHeaders.Authorization = await BearerAsync(userId);
        var own = await client.GetAsync("/api/v1/payments/" + Guid.NewGuid());
        Assert.Equal(HttpStatusCode.NotFound, own.StatusCode);
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQL")]
    public async Task Webhook_with_bad_signature_is_rejected()
    {
        using var client = factory.CreateClient();
        var body = BuildWebhookBody(Guid.NewGuid(), "evt-bad", "idem-bad", 15000);
        var response = await client.PostAsync("/api/v1/webhooks/momo",
            new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> PostWebhookAsync(HttpClient client, string body)
    {
        var signature = Convert.ToHexString(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(StubPaymentGateway.Secret), Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/momo")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("X-MoMo-Signature", signature);
        return await client.SendAsync(request);
    }

    private static string BuildWebhookBody(Guid paymentId, string eventId, string idem, long amount) =>
        JsonSerializer.Serialize(new
        {
            eventId,
            idempotencyKey = idem,
            paymentId = paymentId.ToString(),
            amount,
            currency = "VND",
            orderId = "ignored-by-server",
        });

    private async Task<AuthenticationHeaderValue> BearerAsync(string userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await db.Users.SingleAsync(x => x.Id == userId);
        var session = new Metro.Domain.Entities.Identity.AuthSession
        {
            UserId = userId, RefreshTokenHash = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant(),
            ExpiresAt = DateTime.UtcNow.AddHours(1),
        };
        db.AuthSessions.Add(session);
        await db.SaveChangesAsync();
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(PaymentTestFactory.SigningKey));
        var token = new JwtSecurityToken("metro-api", "metro-web",
            new[] { new Claim(JwtRegisteredClaimNames.Sub, userId), new Claim("security_stamp", user.SecurityStamp!),
                new Claim("session_id", session.Id.ToString()) },
            Now.UtcDateTime, Now.UtcDateTime.AddHours(1),
            new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
    }

    private async Task<(string UserId, Guid OperatorId, Guid OrderId)> SeedAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var opId = Guid.NewGuid();
        scope.ServiceProvider.GetRequiredService<Metro.Application.Abstractions.MultiTenancy.IOperatorContext>()
            .CurrentOperatorId = opId;

        var suffix = Guid.NewGuid().ToString("N");
        var user = new IdentityUser { Id = suffix, UserName = suffix, Email = $"{suffix}@example.invalid", EmailConfirmed = true };
        Assert.True((await users.CreateAsync(user)).Succeeded);

        db.Operators.Add(new Metro.Domain.Entities.Identity.Operator
        {
            Id = opId, Code = "OP-" + suffix, Name = "Payment test operator", Status = "ACTIVE",
        });
        db.OperatorAccounts.Add(new Metro.Domain.Entities.Identity.OperatorAccount
        {
            Id = Guid.NewGuid(), OperatorId = opId, UserId = suffix, Status = "ACTIVE",
        });
        var fareId = Guid.NewGuid();
        db.FarePolicyVersions.Add(new Metro.Domain.Entities.Orders.FarePolicyVersion
        {
            Id = fareId, OperatorId = opId, Code = "FARE-" + suffix, Version = 1, ScopeKey = "SCOPE-" + suffix,
            Status = "PUBLISHED", Currency = "VND", EffectiveFrom = DateTime.UtcNow.AddDays(-1), CreatedByUserId = suffix,
        });
        var productId = Guid.NewGuid();
        db.TicketProducts.Add(new Metro.Domain.Entities.Orders.TicketProduct
        {
            Id = productId, OperatorId = opId, Code = "PROD-" + suffix, Name = "Test product",
            ProductType = "SINGLE_TRIP", ExpiryRule = "SINGLE_JOURNEY", CapacityControlled = false,
        });
        var orderId = Guid.NewGuid();
        db.Orders.Add(new Metro.Domain.Entities.Orders.Order
        {
            Id = orderId, OperatorId = opId, OrderCode = "ORD-" + suffix, Channel = "WEB",
            PayerUserId = suffix, BeneficiaryUserId = suffix, Status = "PENDING_PAYMENT",
            FarePolicyVersionId = fareId, TotalMinor = 15000, SubtotalMinor = 15000,
        });
        db.OrderItems.Add(new Metro.Domain.Entities.Orders.OrderItem
        {
            Id = Guid.NewGuid(), OperatorId = opId, OrderId = orderId, TicketProductId = productId,
            PassengerCount = 1, UnitPriceMinor = 15000, LineTotalMinor = 15000,
        });
        await db.SaveChangesAsync();
        return (suffix, opId, orderId);
    }
}
