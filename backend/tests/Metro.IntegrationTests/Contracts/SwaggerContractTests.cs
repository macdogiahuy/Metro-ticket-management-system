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
        Assert.DoesNotContain("/api/v1/auth/_role-probe", swagger.Paths.Keys);
    }
}
