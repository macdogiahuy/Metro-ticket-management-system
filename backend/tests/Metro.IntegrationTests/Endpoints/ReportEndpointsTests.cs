using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Metro.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Metro.IntegrationTests.Endpoints;

[Collection(DatabaseIntegrationCollection.Name)]
public sealed class ReportEndpointsTests(MetroWebApplicationFactory factory)
{
    private const string Query = "?from=2026-11-01&to=2026-11-30";

    [PostgreSqlFact, Trait("Category", "PostgreSQL")]
    public async Task All_report_routes_require_authentication()
    {
        using var client = factory.CreateClient();
        foreach (var path in new[] { "/api/v1/reports/revenue", "/api/v1/reports/tickets", "/api/v1/reports/revenue/export" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path + Query)).StatusCode);
    }

    [PostgreSqlFact, Trait("Category", "PostgreSQL")]
    public async Task Authenticated_non_admin_is_forbidden()
    {
        using var client = await CreateUserAsync(role: null);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/reports/revenue" + Query)).StatusCode);
    }

    [PostgreSqlFact, Trait("Category", "PostgreSQL")]
    public async Task Admin_without_active_operator_membership_is_forbidden()
    {
        using var client = await CreateUserAsync("Admin");
        var response = await client.GetAsync("/api/v1/reports/revenue" + Query);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("REPORT_OPERATOR_FORBIDDEN", payload.RootElement.GetProperty("code").GetString());
    }

    private async Task<HttpClient> CreateUserAsync(string? role)
    {
        var email = $"report-{Guid.NewGuid():N}@example.invalid";
        var password = $"Metro!{Guid.NewGuid():N}Aa1";
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var user = new IdentityUser { Id = Guid.NewGuid().ToString("N"), UserName = email, Email = email, EmailConfirmed = true };
        Assert.True((await users.CreateAsync(user, password)).Succeeded);
        if (role is not null)
        {
            if (!await roles.RoleExistsAsync(role)) Assert.True((await roles.CreateAsync(new IdentityRole(role))).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
        }
        var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", payload.RootElement.GetProperty("accessToken").GetString());
        return client;
    }
}
