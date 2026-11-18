using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Metro.IntegrationTests.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Metro.IntegrationTests;

public class AuthEndpointsTests : IAsyncLifetime
{
    private const string Password = "MetroDemo2026!";
    private const string SigningKey = "be-03-test-signing-key-0123456789-abcdefghijklmnop";
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        // METRO_TEST_CONNECTION is a keyword/value string accepted as-is by NeonConnectionString.Resolve;
        // it works for both local disposable PostgreSQL in CI and a Neon test database.
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("DATABASE_URL", Environment.GetEnvironmentVariable("METRO_TEST_CONNECTION")!);
            builder.UseSetting("Jwt:SigningKey", SigningKey);
            builder.UseSetting("Jwt:Issuer", "metro-api");
            builder.UseSetting("Jwt:Audience", "metro-web");
            builder.UseSetting("METRO_TEST_MODE", "1");
        });
        _client = _factory.CreateClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [PostgresFact]
    public async Task Register_verify_login_refresh_logout_and_role_policies()
    {
        var email = $"auth-{Guid.NewGuid():N}@example.invalid";
        var register = new
        {
            email, password = Password, fullName = "Auth Test", birthDate = "2000-01-01"
        };

        // Register → 201 + development verification code.
        var response = await _client.PostAsJsonAsync("/api/v1/auth/register", register);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var registered = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var code = registered.RootElement.GetProperty("devVerificationCode").GetString();
        Assert.False(string.IsNullOrWhiteSpace(code));

        // Duplicate email → 400 with stable code.
        response = await _client.PostAsJsonAsync("/api/v1/auth/register", register);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertErrorCodeAsync(response, "EMAIL_ALREADY_EXISTS");

        // Login before verification → 403.
        response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = Password });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertErrorCodeAsync(response, "EMAIL_NOT_VERIFIED");

        // Wrong code → 400 with stable code; correct code → 204.
        response = await _client.PostAsJsonAsync("/api/v1/auth/verify-email", new { email, code = "000000" });
        if (code == "000000")
        {
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }
        else
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            await AssertErrorCodeAsync(response, "INVALID_VERIFICATION_CODE");
            response = await _client.PostAsJsonAsync("/api/v1/auth/verify-email", new { email, code });
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        // Login → tokens; /auth/me requires a token.
        var (access, refresh) = await LoginAsync(email);
        response = await _client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        _client.DefaultRequestHeaders.Authorization = new("Bearer", access);
        response = await _client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var me = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Contains("Passenger", me.RootElement.GetProperty("roles").EnumerateArray().Select(x => x.GetString()));
        _client.DefaultRequestHeaders.Authorization = null;

        // 403 policy: passenger cannot use the staff-only probe; staff can.
        response = await _client.GetAsync("/api/v1/auth/_role-probe");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        _client.DefaultRequestHeaders.Authorization = new("Bearer", access);
        response = await _client.GetAsync("/api/v1/auth/_role-probe");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        _client.DefaultRequestHeaders.Authorization = null;

        var staffEmail = await CreateStaffAsync();
        var (staffAccess, _) = await LoginAsync(staffEmail);
        _client.DefaultRequestHeaders.Authorization = new("Bearer", staffAccess);
        response = await _client.GetAsync("/api/v1/auth/_role-probe");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        _client.DefaultRequestHeaders.Authorization = null;

        // Refresh rotates the token and old refresh token becomes invalid.
        response = await _client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = refresh });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var rotated = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var newRefresh = rotated.RootElement.GetProperty("refreshToken").GetString()!;
        response = await _client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = refresh });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        // Logout revokes the new session; logout again is idempotent.
        response = await _client.PostAsJsonAsync("/api/v1/auth/logout", new { refreshToken = newRefresh });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        response = await _client.PostAsJsonAsync("/api/v1/auth/logout", new { refreshToken = newRefresh });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        response = await _client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = newRefresh });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<(string Access, string Refresh)> LoginAsync(string email)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var tokens = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (tokens.RootElement.GetProperty("accessToken").GetString()!,
                tokens.RootElement.GetProperty("refreshToken").GetString()!);
    }

    private async Task<string> CreateStaffAsync()
    {
        var email = $"staff-{Guid.NewGuid():N}@example.invalid";
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
        Assert.True((await users.CreateAsync(user, Password)).Succeeded);
        if (!await roles.RoleExistsAsync("StationStaff"))
            Assert.True((await roles.CreateAsync(new IdentityRole("StationStaff"))).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, "StationStaff")).Succeeded);
        return email;
    }

    private static async Task AssertErrorCodeAsync(HttpResponseMessage response, string code)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, problem.RootElement.GetProperty("code").GetString());
    }
}
