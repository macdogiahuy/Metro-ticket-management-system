using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Metro.Application.Abstractions.MultiTenancy;
using Metro.Domain.Entities.Identity;
using Metro.Domain.Entities.Orders;
using Metro.Infrastructure.Persistence.Context;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
namespace Metro.IntegrationTests.Payments;

internal static class PaymentScenarioFixture
{
    internal static AuthenticationHeaderValue Bearer(string user)
    {
        var token = new JwtSecurityToken("metro-api", "metro-web", new[] { new Claim(JwtRegisteredClaimNames.Sub, user) },
            DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddHours(1),
            new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(PaymentTestFactory.SigningKey)), SecurityAlgorithms.HmacSha256));
        return new("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
    }
    internal static async Task<string> UserAsync(IServiceProvider services)
    {
        var users = services.GetRequiredService<UserManager<IdentityUser>>(); var id = Guid.NewGuid().ToString("N");
        Assert.True((await users.CreateAsync(new IdentityUser { Id = id, UserName = id, Email = $"{id}@example.invalid", EmailConfirmed = true })).Succeeded);
        var db = services.GetRequiredService<ApplicationDbContext>();
        db.UserProfiles.Add(new UserProfile { UserId = id, FullName = "Integration review", NormalizedFullName = "INTEGRATION REVIEW", BirthDate = new DateOnly(1990, 1, 1), Status = "ACTIVE" });
        await db.SaveChangesAsync(); return id;
    }
    internal static async Task<PaymentScenario> SeedAsync(WebApplicationFactory<Program> app, bool membership)
    {
        await using var scope = app.Services.CreateAsyncScope(); var services = scope.ServiceProvider;
        var user = await UserAsync(services); var op = Guid.NewGuid(); var code = "RV-" + Guid.NewGuid().ToString("N");
        services.GetRequiredService<IOperatorContext>().CurrentOperatorId = op;
        var db = services.GetRequiredService<ApplicationDbContext>();
        var fare = Guid.NewGuid(); var product = Guid.NewGuid(); var order = Guid.NewGuid();
        db.Operators.Add(new Operator { Id = op, Code = code, Name = "BE09 review", Status = "ACTIVE" });
        if (membership) db.OperatorAccounts.Add(new OperatorAccount { Id = Guid.NewGuid(), OperatorId = op, UserId = user, Status = "ACTIVE" });
        db.FarePolicyVersions.Add(new FarePolicyVersion { Id = fare, OperatorId = op, Code = "FARE", Version = 1, ScopeKey = "REVIEW", CreatedByUserId = user, EffectiveFrom = DateTime.UtcNow.AddDays(-1) });
        db.TicketProducts.Add(new TicketProduct { Id = product, OperatorId = op, Code = "DAY", Name = "Day pass", ProductType = "DAY", DurationValue = 1, DurationUnit = "DAY", ExpiryRule = "NEXT_LOCAL_MIDNIGHT" });
        var day = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        db.Orders.Add(new Order { Id = order, OperatorId = op, OrderCode = "ORDER", Channel = "WEB", PayerUserId = user, BeneficiaryUserId = user, FarePolicyVersionId = fare, TotalMinor = 15000, SubtotalMinor = 15000, Status = "PENDING_PAYMENT", ServiceDate = day });
        await db.SaveChangesAsync();
        db.OrderItems.Add(new OrderItem { OperatorId = op, OrderId = order, TicketProductId = product, UnitPriceMinor = 15000, LineTotalMinor = 15000 });
        db.OrderVersions.Add(new OrderVersion { OperatorId = op, OrderId = order, Version = 1, Status = "CONFIRMED", ConfirmedAt = DateTime.UtcNow, TicketProductId = product, BeneficiaryUserId = user, ServiceDate = day, SubtotalMinor = 15000, TotalMinor = 15000, FarePolicyVersionId = fare });
        await db.SaveChangesAsync(); return new(user, op, code, order);
    }
    internal sealed record PaymentScenario(string User, Guid Op, string Code, Guid Order);
}
