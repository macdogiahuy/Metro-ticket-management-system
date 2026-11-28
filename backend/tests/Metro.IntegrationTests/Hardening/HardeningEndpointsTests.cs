using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Metro.Domain.Entities.Identity;
using Metro.Infrastructure.Persistence.Context;
using Metro.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Metro.IntegrationTests.Hardening;

[Collection(DatabaseIntegrationCollection.Name)]
public sealed class HardeningEndpointsTests(MetroWebApplicationFactory factory)
{
    [PostgreSqlFact]
    public async Task Security_headers_are_present_on_public_and_sensitive_routes()
    {
        using var client = factory.CreateClient();
        var health = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal("nosniff", health.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", health.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", health.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("frame-ancestors 'none'", health.Headers.GetValues("Content-Security-Policy").Single());

        var anonymous = await client.GetAsync("/api/v1/admin/accounts");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal("no-store", anonymous.Headers.CacheControl?.ToString());

        var passenger = await CreateAccountAsync("Passenger");
        using var passengerClient = Client(passenger);
        var forbidden = await passengerClient.GetAsync("/api/v1/admin/accounts");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal("no-store", forbidden.Headers.CacheControl?.ToString());
    }

    [PostgreSqlFact]
    public async Task Rate_limit_returns_429_problem_details_after_configured_burst()
    {
        using var limited = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Hardening:RateLimiting:PermitLimit", "2");
            b.UseSetting("Hardening:RateLimiting:WindowSeconds", "60");
        }).CreateClient();

        var body = new { email = "no-such-user@example.invalid", password = "wrong-password" };
        var first = await limited.PostAsJsonAsync("/api/v1/auth/login", body);
        var second = await limited.PostAsJsonAsync("/api/v1/auth/login", body);
        var third = await limited.PostAsJsonAsync("/api/v1/auth/login", body);

        Assert.Equal(HttpStatusCode.Unauthorized, first.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.Equal("60", third.Headers.RetryAfter?.Delta?.TotalSeconds.ToString("0") ?? "60");
        using var problem = JsonDocument.Parse(await third.Content.ReadAsStringAsync());
        Assert.Equal("RATE_LIMITED", problem.RootElement.GetProperty("code").GetString());
    }

    [PostgreSqlFact]
    public async Task Hardening_headers_survive_a_light_concurrent_load()
    {
        using var client = factory.CreateClient();
        var latencies = new List<double>();
        var gate = new SemaphoreSlim(10);
        var requests = Enumerable.Range(0, 60).Select(async _ =>
        {
            await gate.WaitAsync();
            try
            {
                var stopwatch = Stopwatch.StartNew();
                var response = await client.GetAsync("/health/live");
                stopwatch.Stop();
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
                lock (latencies) latencies.Add(stopwatch.Elapsed.TotalMilliseconds);
            }
            finally { gate.Release(); }
        });
        await Task.WhenAll(requests);

        latencies.Sort();
        var p95 = latencies[(int)Math.Ceiling(latencies.Count * 0.95) - 1];
        Assert.True(p95 < 1000, $"P95 latency {p95:F1} ms exceeded the 1000 ms smoke budget.");
    }

    [PostgreSqlFact]
    public async Task Successful_admin_write_always_produces_an_audit_record()
    {
        var admin = await CreateAccountAsync("Admin");
        var target = await CreateAccountAsync("Passenger");
        using var client = Client(admin);

        var correlation = Guid.NewGuid().ToString("N");
        client.DefaultRequestHeaders.Add("X-Correlation-Id", correlation);
        var response = await client.PutAsJsonAsync($"/api/v1/admin/accounts/{target.Id}/status",
            new { status = "LOCKED", reason = "BE-17 audit guard" });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(correlation, response.Headers.GetValues("X-Correlation-Id").SingleOrDefault());

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var guardAudit = await db.AuditLogs.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x =>
            x.Action == "ADMIN_WRITE_AUDITED" && x.CorrelationId == Guid.Parse(correlation));
        Assert.NotNull(guardAudit);
        Assert.Equal("http_request", guardAudit.EntityType);
        Assert.Equal(admin.Id, guardAudit.ActorUserId);
    }

    private HttpClient Client(Account account)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", account.AccessToken);
        return client;
    }

    private async Task<Account> CreateAccountAsync(string role)
    {
        var email = $"be17-{Guid.NewGuid():N}@example.invalid";
        var password = $"Metro!{Guid.NewGuid():N}Aa1";
        string id;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
            Assert.True((await users.CreateAsync(user, password)).Succeeded);
            foreach (var name in new[] { "Passenger", role }.Distinct())
            {
                if (!await roles.RoleExistsAsync(name))
                    Assert.True((await roles.CreateAsync(new IdentityRole(name))).Succeeded);
                Assert.True((await users.AddToRoleAsync(user, name)).Succeeded);
            }
            id = user.Id;
            db.UserProfiles.Add(new UserProfile
            {
                UserId = id, FullName = "Hardening Test", NormalizedFullName = "HARDENING TEST",
                BirthDate = new DateOnly(2000, 1, 1), Status = "ACTIVE",
            });
            await db.SaveChangesAsync();
        }

        using var login = factory.CreateClient();
        var response = await login.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var tokens = (await response.Content.ReadFromJsonAsync<Tokens>())!;
        return new Account(id, tokens.AccessToken);
    }

    private sealed record Tokens(string AccessToken, string RefreshToken);
    private sealed record Account(string Id, string AccessToken);
}
