using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Metro.Infrastructure.Persistence.Context;
using Metro.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Metro.IntegrationTests.Endpoints;

[Collection(DatabaseIntegrationCollection.Name)]
public sealed class AuthEndpointsTests(MetroWebApplicationFactory factory)
{
    [PostgreSqlFact]
    [Trait("Category", "PostgreSQL")]
    public async Task Register_persists_data_and_rejects_duplicate_email()
    {
        using var client = factory.CreateClient();
        var account = await RegisterAsync(client, UniqueEmail("persist"), "Database Integration Test");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await users.FindByEmailAsync(account.Email);
            Assert.NotNull(user);
            Assert.Equal(account.UserId, user.Id);
            Assert.Equal("Database Integration Test",
                (await db.UserProfiles.AsNoTracking().SingleAsync(x => x.UserId == account.UserId)).FullName);
            Assert.NotNull(await db.EmailVerificationChallenges.AsNoTracking()
                .SingleOrDefaultAsync(x => x.UserId == account.UserId && x.Purpose == "VERIFY_EMAIL"));
        }

        var duplicate = await client.PostAsJsonAsync("/api/v1/auth/register", RegistrationBody(account));
        await AssertProblemAsync(duplicate, HttpStatusCode.BadRequest, "EMAIL_ALREADY_EXISTS");
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQL")]
    public async Task Auth_flow_preserves_verification_role_refresh_and_logout_contracts()
    {
        using var client = factory.CreateClient();
        var passenger = await RegisterAsync(client, UniqueEmail("flow"));

        var response = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = passenger.Email, password = passenger.Password });
        await AssertProblemAsync(response, HttpStatusCode.Forbidden, "EMAIL_NOT_VERIFIED");

        var wrongCode = passenger.VerificationCode == "000000" ? "999999" : "000000";
        response = await client.PostAsJsonAsync("/api/v1/auth/verify-email",
            new { email = passenger.Email, code = wrongCode });
        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "INVALID_VERIFICATION_CODE");

        response = await client.PostAsJsonAsync("/api/v1/auth/verify-email",
            new { email = passenger.Email, code = passenger.VerificationCode });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var passengerTokens = await LoginAsync(client, passenger);

        response = await client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", passengerTokens.AccessToken);
        response = await client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var me = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
            Assert.Contains("Passenger", me.RootElement.GetProperty("roles").EnumerateArray().Select(x => x.GetString()));
        response = await client.GetAsync("/api/v1/auth/_role-probe");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var staff = await CreateStaffAsync();
        var staffTokens = await LoginAsync(client, staff);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffTokens.AccessToken);
        response = await client.GetAsync("/api/v1/auth/_role-probe");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        client.DefaultRequestHeaders.Authorization = null;

        var originalSession = Assert.Single(await LoadSessionsAsync(passenger.UserId), x => x.RevokedAt == null);
        response = await client.PostAsJsonAsync("/api/v1/auth/refresh",
            new { refreshToken = passengerTokens.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rotatedTokens = await ReadTokensAsync(response);

        var afterRotation = await LoadSessionsAsync(passenger.UserId);
        var rotatedSession = Assert.Single(afterRotation, x => x.Id == originalSession.Id);
        Assert.Equal("ROTATED", rotatedSession.RevocationReason);
        Assert.Single(afterRotation, x => x.RevokedAt == null);

        response = await client.PostAsJsonAsync("/api/v1/auth/refresh",
            new { refreshToken = passengerTokens.RefreshToken });
        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "INVALID_REFRESH_TOKEN");

        response = await client.PostAsJsonAsync("/api/v1/auth/logout",
            new { refreshToken = rotatedTokens.RefreshToken });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var logoutSession = Assert.Single(await LoadSessionsAsync(passenger.UserId), x => x.RevocationReason == "LOGOUT");

        response = await client.PostAsJsonAsync("/api/v1/auth/logout",
            new { refreshToken = rotatedTokens.RefreshToken });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var afterReplay = await LoadSessionsAsync(passenger.UserId);
        Assert.Equal(logoutSession.RevokedAt, Assert.Single(afterReplay, x => x.Id == logoutSession.Id).RevokedAt);
        Assert.Equal(2, afterReplay.Length);
        Assert.DoesNotContain(afterReplay, x => x.RevokedAt == null);

        response = await client.PostAsJsonAsync("/api/v1/auth/refresh",
            new { refreshToken = rotatedTokens.RefreshToken });
        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "INVALID_REFRESH_TOKEN");
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQL")]
    public async Task Authentication_logs_do_not_contain_pii_or_secrets()
    {
        using var client = factory.CreateClient();
        var account = await RegisterAsync(client, UniqueEmail("private"));
        var response = await client.PostAsJsonAsync("/api/v1/auth/verify-email",
            new { email = account.Email, code = account.VerificationCode });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var tokens = await LoginAsync(client, account);

        var logs = string.Join(Environment.NewLine, factory.TestLogs.Messages);
        Assert.DoesNotContain(account.Email, logs, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(account.Password, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(account.VerificationCode, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(tokens.AccessToken, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(tokens.RefreshToken, logs, StringComparison.Ordinal);
    }

    private static async Task<RegisteredAccount> RegisterAsync(HttpClient client, string email,
        string fullName = "Auth Integration Test")
    {
        var account = new RegisteredAccount(string.Empty, email, UniquePassword(), string.Empty, fullName);
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", RegistrationBody(account));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return account with
        {
            UserId = payload.RootElement.GetProperty("userId").GetString()!,
            VerificationCode = payload.RootElement.GetProperty("devVerificationCode").GetString()!
        };
    }

    private async Task<RegisteredAccount> CreateStaffAsync()
    {
        var account = new RegisteredAccount(Guid.NewGuid().ToString("N"), UniqueEmail("staff"),
            UniquePassword(), string.Empty, "Station Staff Test");
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var user = new IdentityUser
        {
            Id = account.UserId,
            UserName = account.Email,
            Email = account.Email,
            EmailConfirmed = true
        };
        Assert.True((await users.CreateAsync(user, account.Password)).Succeeded);
        if (!await roles.RoleExistsAsync("StationStaff"))
            Assert.True((await roles.CreateAsync(new IdentityRole("StationStaff"))).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, "StationStaff")).Succeeded);
        return account;
    }

    private static async Task<TokenPair> LoginAsync(HttpClient client, RegisteredAccount account)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { email = account.Email, password = account.Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadTokensAsync(response);
    }

    private async Task<SessionState[]> LoadSessionsAsync(string userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.AuthSessions.AsNoTracking().Where(x => x.UserId == userId)
            .OrderBy(x => x.IssuedAt)
            .Select(x => new SessionState(x.Id, x.RevokedAt, x.RevocationReason))
            .ToArrayAsync();
    }

    private static async Task<TokenPair> ReadTokensAsync(HttpResponseMessage response)
    {
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return new TokenPair(payload.RootElement.GetProperty("accessToken").GetString()!,
            payload.RootElement.GetProperty("refreshToken").GetString()!);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, problem.RootElement.GetProperty("code").GetString());
    }

    private static object RegistrationBody(RegisteredAccount account) => new
    {
        email = account.Email,
        password = account.Password,
        fullName = account.FullName,
        birthDate = "2000-01-01"
    };

    private static string UniqueEmail(string prefix) => $"{prefix}-{Guid.NewGuid():N}@example.invalid";
    private static string UniquePassword() => $"Metro!{Guid.NewGuid():N}Aa1";

    private sealed record RegisteredAccount(
        string UserId, string Email, string Password, string VerificationCode, string FullName);
    private sealed record TokenPair(string AccessToken, string RefreshToken);
    private sealed record SessionState(Guid Id, DateTime? RevokedAt, string? RevocationReason);
}
