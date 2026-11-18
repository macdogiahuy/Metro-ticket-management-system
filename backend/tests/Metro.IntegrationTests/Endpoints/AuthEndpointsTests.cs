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
    public async Task Register_persists_identity_profile_and_verification_challenge()
    {
        using var client = factory.CreateClient();
        var email = UniqueEmail("persist");
        var password = UniquePassword();

        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email,
            password,
            fullName = "Database Integration Test",
            birthDate = "2000-01-01"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var userId = payload.RootElement.GetProperty("userId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(userId));

        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await users.FindByEmailAsync(email);

        Assert.NotNull(user);
        Assert.Equal(userId, user.Id);
        Assert.Equal("Database Integration Test",
            (await db.UserProfiles.AsNoTracking().SingleAsync(x => x.UserId == userId)).FullName);
        Assert.NotNull(await db.EmailVerificationChallenges.AsNoTracking()
            .SingleOrDefaultAsync(x => x.UserId == userId && x.Purpose == "VERIFY_EMAIL"));
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQL")]
    public async Task Protected_endpoints_return_401_and_403_for_missing_or_insufficient_identity()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var passenger = await RegisterVerifyAndLoginAsync(client, UniqueEmail("passenger"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", passenger.AccessToken);

        response = await client.GetAsync("/api/v1/auth/_role-probe");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQL")]
    public async Task Repeated_logout_has_one_database_effect()
    {
        using var client = factory.CreateClient();
        var account = await RegisterVerifyAndLoginAsync(client, UniqueEmail("logout"));
        var sessionId = await FindActiveSessionIdAsync(account.UserId);

        var response = await client.PostAsJsonAsync("/api/v1/auth/logout", new { refreshToken = account.RefreshToken });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var firstRevokedAt = await FindRevokedAtAsync(sessionId);
        Assert.NotNull(firstRevokedAt);

        response = await client.PostAsJsonAsync("/api/v1/auth/logout", new { refreshToken = account.RefreshToken });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        Assert.Equal(firstRevokedAt, await FindRevokedAtAsync(sessionId));
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.AuthSessions.CountAsync(x => x.UserId == account.UserId));
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQL")]
    public async Task Authentication_logs_do_not_contain_pii_or_secrets()
    {
        using var client = factory.CreateClient();
        var email = UniqueEmail("private");
        var account = await RegisterVerifyAndLoginAsync(client, email);

        var logs = string.Join(Environment.NewLine, factory.TestLogs.Messages);
        Assert.DoesNotContain(email, logs, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(account.Password, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(account.VerificationCode, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(account.AccessToken, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(account.RefreshToken, logs, StringComparison.Ordinal);
    }

    private async Task<Guid> FindActiveSessionIdAsync(string userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.AuthSessions.AsNoTracking()
            .Where(x => x.UserId == userId && x.RevokedAt == null)
            .Select(x => x.Id)
            .SingleAsync();
    }

    private async Task<DateTime?> FindRevokedAtAsync(Guid sessionId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.AuthSessions.AsNoTracking()
            .Where(x => x.Id == sessionId)
            .Select(x => x.RevokedAt)
            .SingleAsync();
    }

    private static async Task<TestAccount> RegisterVerifyAndLoginAsync(HttpClient client, string email)
    {
        var password = UniquePassword();
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email,
            password,
            fullName = "Auth Integration Test",
            birthDate = "2000-01-01"
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var registered = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var userId = registered.RootElement.GetProperty("userId").GetString()!;
        var code = registered.RootElement.GetProperty("devVerificationCode").GetString()!;

        response = await client.PostAsJsonAsync("/api/v1/auth/verify-email", new { email, code });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var tokens = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return new TestAccount(userId, password, code,
            tokens.RootElement.GetProperty("accessToken").GetString()!,
            tokens.RootElement.GetProperty("refreshToken").GetString()!);
    }

    private static string UniqueEmail(string prefix) => $"{prefix}-{Guid.NewGuid():N}@example.invalid";

    private static string UniquePassword() => $"Metro!{Guid.NewGuid():N}Aa1";

    private sealed record TestAccount(
        string UserId,
        string Password,
        string VerificationCode,
        string AccessToken,
        string RefreshToken);
}
