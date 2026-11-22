using Metro.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Swagger;

namespace Metro.IntegrationTests.Contracts;

[Collection(DatabaseIntegrationCollection.Name)]
public sealed class SwaggerContractTests(MetroWebApplicationFactory factory)
{
    [PostgreSqlFact]
    public void OpenApi_contains_current_health_and_auth_contracts()
    {
        var swagger = factory.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");

        Assert.Contains("/health", swagger.Paths.Keys);
        Assert.Contains("/health/live", swagger.Paths.Keys);
        Assert.Contains("/api/v1/auth/register", swagger.Paths.Keys);
        Assert.Contains("/api/v1/auth/login", swagger.Paths.Keys);
        Assert.Contains("/api/v1/auth/me", swagger.Paths.Keys);
        Assert.Contains("/api/v1/lines", swagger.Paths.Keys);
        Assert.Contains("/api/v1/stations", swagger.Paths.Keys);
        Assert.Contains("/api/v1/journeys", swagger.Paths.Keys);
        Assert.Contains("/api/v1/service-status", swagger.Paths.Keys);
        Assert.Contains("/api/v1/payments", swagger.Paths.Keys);
        Assert.Contains("/api/v1/payments/{id}/retry", swagger.Paths.Keys);
        Assert.Contains("/api/v1/webhooks/momo", swagger.Paths.Keys);
        Assert.DoesNotContain("/api/v1/auth/_role-probe", swagger.Paths.Keys);
        foreach (var path in new[] { "/api/v1/tickets", "/api/v1/tickets/{id}", "/api/v1/tickets/{id}/qr", "/api/v1/orders", "/api/v1/payments", "/api/v1/orders/{id}/issue-free" })
            Assert.Contains(path, swagger.Paths.Keys);
        var free = swagger.Paths["/api/v1/orders/{id}/issue-free"].Operations[Microsoft.OpenApi.Models.OperationType.Post];
        Assert.Contains(free.Parameters, p => p.Name == "Idempotency-Key" && p.Required);
        Assert.Contains("202", free.Responses.Keys);
        Assert.NotEmpty(free.Security);
        var qr = swagger.Paths["/api/v1/tickets/{id}/qr"].Operations[Microsoft.OpenApi.Models.OperationType.Get];
        Assert.Contains("image/png", qr.Responses["200"].Content.Keys);
    }
}
