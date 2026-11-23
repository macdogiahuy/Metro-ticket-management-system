using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Metro.Domain.Entities.Identity;
using Metro.Infrastructure.Persistence.Context;
using Metro.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Metro.IntegrationTests.Endpoints;

[Collection(DatabaseIntegrationCollection.Name)]
public sealed class AdministrationEndpointsTests(MetroWebApplicationFactory factory)
{
    [PostgreSqlFact]
    [Trait("Category", "PostgreSQL")]
    public async Task Administration_requires_admin_and_audits_rejected_staff_access()
    {
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/admin/accounts")).StatusCode);
        foreach (var role in new[] { "Passenger", "StationStaff" })
        {
            var account = await CreateAccountAsync(role);
            using var client = Client(account);
            foreach (var path in new[] { "/accounts", "/roles", "/passengers/another-user?purpose=Support" })
                Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/admin" + path)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/api/v1/admin/accounts/{account.Id}/roles",
                new { roles = new[] { "Admin" }, reason = "Escalate" })).StatusCode);
            var audits = await AuditsAsync(account.Id, "ADMIN_ACCESS_DENIED");
            Assert.Equal(4, audits.Length);
            Assert.All(audits, x => Assert.Equal("DENIED", x.Outcome));
        }
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQL")]
    public async Task Search_is_paged_filtered_verified_only_and_does_not_expose_identity_secrets()
    {
        var admin = await CreateAccountAsync("Admin");
        var target = await CreateAccountAsync("Passenger");
        var pending = await CreateAccountAsync("Passenger", verified: false);
        using var client = Client(admin);
        var response = await client.GetAsync($"/api/v1/admin/accounts?search={target.Email}&role=Passenger&pageSize=1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadAsStringAsync();
        using var payload = JsonDocument.Parse(page);
        Assert.Equal(1, payload.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(target.Id, Assert.Single(payload.RootElement.GetProperty("items").EnumerateArray()).GetProperty("userId").GetString());
        Assert.DoesNotContain("passwordHash", page);
        Assert.DoesNotContain("securityStamp", page);
        Assert.DoesNotContain("citizenId", page);
        using var missing = JsonDocument.Parse(await client.GetStringAsync($"/api/v1/admin/accounts?search={pending.Email}"));
        Assert.Equal(0, missing.RootElement.GetProperty("total").GetInt32());
        foreach (var query in new[] { "page=0", "pageSize=101", "role=Guest", "status=UNKNOWN", "page=2147483647&pageSize=100" })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/admin/accounts?" + query)).StatusCode);
        Assert.Equal(2, (await AuditsAsync(admin.Id, "ACCOUNTS_SEARCHED")).Length);
        var roles = await client.GetFromJsonAsync<JsonElement>("/api/v1/admin/roles");
        Assert.Equal(new[] { "Passenger", "StationStaff", "Admin" }, roles.EnumerateArray().Select(x => x.GetProperty("code").GetString()));
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQL")]
    public async Task Role_change_revokes_all_access_and_refresh_tokens_and_retains_passenger_permissions()
    {
        var admin = await CreateAccountAsync("Admin");
        var target = await CreateAccountAsync("Passenger");
        var second = await LoginAsync(factory, target.Email, target.Password);
        using var client = Client(admin);
        var response = await client.PutAsJsonAsync($"/api/v1/admin/accounts/{target.Id}/roles",
            new { roles = new[] { "StationStaff" }, reason = "Assigned inspector duties" });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var oldClient = Client(target);
        Assert.Equal(HttpStatusCode.Unauthorized, (await oldClient.GetAsync("/api/v1/me")).StatusCode);
        foreach (var refresh in new[] { target.RefreshToken, second.RefreshToken })
            Assert.Equal(HttpStatusCode.Unauthorized, (await oldClient.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = refresh })).StatusCode);
        var current = await LoginAsync(factory, target.Email, target.Password);
        using var staff = Client(target with { AccessToken = current.AccessToken });
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync("/api/v1/auth/_role-probe")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/v1/admin/accounts")).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        Assert.Equal(new[] { "Passenger", "StationStaff" }, (await users.GetRolesAsync((await users.FindByIdAsync(target.Id))!)).Order().ToArray());
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(2, await db.AuthSessions.CountAsync(x => x.UserId == target.Id && x.RevocationReason == "ROLES_CHANGED"));
        var audit = Assert.Single(await AuditsAsync(admin.Id, "ACCOUNT_ROLES_CHANGED"));
        Assert.Equal(target.Id, audit.EntityId);
        Assert.NotEqual(audit.BeforeHash, audit.AfterHash);
        Assert.Matches("^[0-9a-f]{64}$", audit.BeforeHash!);
        // Exact replay is a no-op, including its audit, outbox and new session.
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"/api/v1/admin/accounts/{target.Id}/roles",
            new { roles = new[] { "StationStaff" }, reason = "Assigned inspector duties" })).StatusCode);
        Assert.Single(await AuditsAsync(admin.Id, "ACCOUNT_ROLES_CHANGED"));
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync("/api/v1/me")).StatusCode);
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQL")]
    public async Task Lock_blocks_login_refresh_and_existing_access_and_unlock_requires_a_new_login()
    {
        var admin = await CreateAccountAsync("Admin");
        var target = await CreateAccountAsync("Passenger");
        using var client = Client(admin);
        using var passenger = Client(target);
        Assert.Equal(HttpStatusCode.NoContent, (await StatusAsync(client, target.Id, "LOCKED")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await passenger.GetAsync("/api/v1/me")).StatusCode);
        await ProblemAsync(await passenger.PostAsJsonAsync("/api/v1/auth/login", new { email = target.Email, password = target.Password }), HttpStatusCode.Forbidden, "ACCOUNT_LOCKED");
        Assert.Equal(HttpStatusCode.Unauthorized, (await passenger.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = target.RefreshToken })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await StatusAsync(client, target.Id, "ACTIVE")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await passenger.GetAsync("/api/v1/me")).StatusCode);
        await LoginAsync(factory, target.Email, target.Password);
        Assert.Single(await AuditsAsync(admin.Id, "ACCOUNT_LOCKED"));
        Assert.Single(await AuditsAsync(admin.Id, "ACCOUNT_UNLOCKED"));
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQL")]
    public async Task Invalid_roles_reasons_unverified_users_and_unknown_fields_are_rejected()
    {
        var admin = await CreateAccountAsync("Admin");
        var target = await CreateAccountAsync("Passenger");
        var pending = await CreateAccountAsync("Passenger", verified: false);
        using var client = Client(admin);
        foreach (var invalid in new[] { Array.Empty<string>(), new[] { "Guest" }, new[] { "Admin", "StationStaff" }, new[] { "Admin", "Admin" }, new string[] { null! } })
            await ProblemAsync(await client.PutAsJsonAsync($"/api/v1/admin/accounts/{target.Id}/roles", new { roles = invalid, reason = "Test" }), HttpStatusCode.BadRequest, "INVALID_ROLES");
        await ProblemAsync(await client.PutAsJsonAsync($"/api/v1/admin/accounts/{pending.Id}/roles", new { roles = new[] { "Admin" }, reason = "Test" }), HttpStatusCode.Conflict, "EMAIL_NOT_VERIFIED");
        await ProblemAsync(await StatusAsync(client, target.Id, "CLOSED"), HttpStatusCode.BadRequest, "INVALID_ACCOUNT_STATUS");
        await ProblemAsync(await client.PutAsJsonAsync($"/api/v1/admin/accounts/{target.Id}/status", new { status = "LOCKED", reason = " " }), HttpStatusCode.BadRequest, "REASON_REQUIRED");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/v1/admin/passengers/{target.Id}",
            new { fullName = "New name", birthDate = "2000-01-01", reason = "Correction", email = "replacement@example.invalid" })).StatusCode);
        Assert.Empty(await AuditsAsync(admin.Id, "ACCOUNT_ROLES_CHANGED"));
        await ProblemAsync(await StatusAsync(client, "missing", "LOCKED"), HttpStatusCode.NotFound, "USER_NOT_FOUND");
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQL")]
    public async Task Passenger_access_requires_purpose_masks_pii_and_is_audited_before_returning()
    {
        var admin = await CreateAccountAsync("Admin");
        var target = await CreateAccountAsync("Passenger");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var profile = await db.UserProfiles.SingleAsync(x => x.UserId == target.Id);
            profile.CitizenIdLast4 = "1234";
            profile.BankAccountLast4 = "6789";
            profile.CitizenIdCiphertext = [1, 2, 3];
            profile.CitizenIdLookupHash = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
            profile.BankAccountCiphertext = [4, 5, 6];
            profile.BankCode = "TEST-" + Guid.NewGuid().ToString("N");
            profile.BankHolderName = "Synthetic Holder";
            db.Banks.Add(new Bank { Code = profile.BankCode, Name = "Synthetic Bank" });
            (await db.Users.SingleAsync(x => x.Id == target.Id)).PhoneNumber = "+84912345678";
            await db.SaveChangesAsync();
        }
        using var client = Client(admin);
        await ProblemAsync(await client.GetAsync($"/api/v1/admin/passengers/{target.Id}"), HttpStatusCode.BadRequest, "PURPOSE_REQUIRED");
        var response = await client.GetAsync($"/api/v1/admin/passengers/{target.Id}?purpose=Resolve%20support%20case");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
        var json = await response.Content.ReadAsStringAsync();
        using var payload = JsonDocument.Parse(json);
        Assert.Equal("****1234", payload.RootElement.GetProperty("citizenId").GetString());
        Assert.Equal("****6789", payload.RootElement.GetProperty("bankAccount").GetString());
        Assert.Equal("****5678", payload.RootElement.GetProperty("phoneNumber").GetString());
        Assert.DoesNotContain("Ciphertext", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("objectKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("+84912345678", json);
        Assert.Single(await AuditsAsync(admin.Id, "PASSENGER_VIEWED"));
        Assert.Equal("DENIED", Assert.Single(await AuditsAsync(admin.Id, "PASSENGER_ACCESS_DENIED")).Outcome);
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQL")]
    public async Task Correcting_verified_identity_requires_review_preserves_decision_history_and_is_idempotent()
    {
        var admin = await CreateAccountAsync("Admin");
        var target = await CreateAccountAsync("Passenger");
        var evidenceId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var profile = await db.UserProfiles.SingleAsync(x => x.UserId == target.Id);
            profile.CitizenIdVerifiedAt = DateTime.UtcNow;
            profile.CitizenIdCiphertext = [1, 2, 3];
            profile.CitizenIdLookupHash = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
            profile.CitizenIdLast4 = "1234";
            db.EligibilityEvidence.Add(new EligibilityEvidence
            {
                Id = evidenceId, UserId = target.Id, Category = "STUDENT", Status = "VERIFIED",
                ValidFrom = today, ValidTo = today.AddMonths(3), DecidedAt = DateTime.UtcNow,
                DecidedByUserId = admin.Id, DecisionReason = "Verified original",
                Decisions = [new EligibilityDecision { RequestId = evidenceId, Status = "VERIFIED", Reason = "Verified original", ActorUserId = admin.Id }],
            });
            await db.SaveChangesAsync();
        }
        using var client = Client(admin);
        var request = new { fullName = "Corrected Passenger", birthDate = "2001-02-03", reason = "Correct identity from reviewed evidence" };
        for (var i = 0; i < 2; i++)
            Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"/api/v1/admin/passengers/{target.Id}", request)).StatusCode);
        await using var check = factory.Services.CreateAsyncScope();
        var saved = check.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var profileAfter = await saved.UserProfiles.AsNoTracking().SingleAsync(x => x.UserId == target.Id);
        Assert.Equal("Corrected Passenger", profileAfter.FullName);
        Assert.Null(profileAfter.CitizenIdVerifiedAt);
        var evidence = await saved.EligibilityEvidence.AsNoTracking().Include(x => x.Decisions).SingleAsync(x => x.Id == evidenceId);
        Assert.Equal("NEEDS_INFO", evidence.Status);
        Assert.Equal(today.AddMonths(3), evidence.ValidTo);
        Assert.Equal(2, evidence.Decisions.Count);
        Assert.Contains(evidence.Decisions, x => x.Status == "VERIFIED");
        Assert.Contains(evidence.Decisions, x => x.Status == "NEEDS_INFO");
        var audit = Assert.Single(await AuditsAsync(admin.Id, "PASSENGER_UPDATED"));
        Assert.NotEqual(audit.BeforeHash, audit.AfterHash);
        Assert.Single(await AuditsAsync(admin.Id, "ELIGIBILITY_REVIEW_REQUIRED"));
        using var passenger = Client(target);
        Assert.Equal(HttpStatusCode.Conflict, (await passenger.PutAsJsonAsync("/api/v1/me",
            new { fullName = "Bypass review", birthDate = "2001-02-03", locale = "vi-VN", emailNotifications = true })).StatusCode);
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQL")]
    public async Task Last_active_administrator_is_preserved_even_for_concurrent_self_demotions()
    {
        var first = await CreateAccountAsync("Admin");
        var second = await CreateAccountAsync("Admin");
        var hidden = await HideOtherAdministratorsAsync(first.Id, second.Id);
        try
        {
            using var one = Client(first);
            using var two = Client(second);
            var responses = await Task.WhenAll(
                one.PutAsJsonAsync($"/api/v1/admin/accounts/{first.Id}/roles", new { roles = new[] { "Passenger" }, reason = "Leave duties" }),
                two.PutAsJsonAsync($"/api/v1/admin/accounts/{second.Id}/roles", new { roles = new[] { "Passenger" }, reason = "Leave duties" }));
            Assert.Single(responses, x => x.StatusCode == HttpStatusCode.NoContent);
            await ProblemAsync(Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict), HttpStatusCode.Conflict, "LAST_ACTIVE_ADMIN");
            var remaining = responses[0].StatusCode == HttpStatusCode.Conflict ? first : second;
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await (responses[0].StatusCode == HttpStatusCode.NoContent ? one : two).GetAsync("/api/v1/admin/roles")).StatusCode);
            using var last = Client(remaining);
            await ProblemAsync(await StatusAsync(last, remaining.Id, "LOCKED"), HttpStatusCode.Conflict, "LAST_ACTIVE_ADMIN");
            Assert.Equal(HttpStatusCode.OK, (await last.GetAsync("/api/v1/admin/roles")).StatusCode);
        }
        finally { await RestoreAdministratorsAsync(hidden); }
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQL")]
    public async Task Commit_failure_rolls_back_roles_sessions_audit_and_notifications()
    {
        var admin = await CreateAccountAsync("Admin");
        var target = await CreateAccountAsync("Passenger");
        using var failing = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddDbContext<ApplicationDbContext>(options => options.AddInterceptors(new FailAdminCommit()))));
        using var client = Client(admin, failing);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/v1/admin/accounts/{target.Id}/roles",
            new { roles = new[] { "Admin" }, reason = "Simulated failure" })).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        Assert.False(await users.IsInRoleAsync((await users.FindByIdAsync(target.Id))!, "Admin"));
        Assert.Empty(await AuditsAsync(admin.Id, "ACCOUNT_ROLES_CHANGED"));
        Assert.Single(await db.AuthSessions.Where(x => x.UserId == target.Id && x.RevokedAt == null).ToArrayAsync());
        var notifications = await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM metro.notifications WHERE recipient_user_id = {target.Id}").SingleAsync();
        Assert.Equal(0, notifications);
        using var passenger = Client(target);
        Assert.Equal(HttpStatusCode.OK, (await passenger.GetAsync("/api/v1/me")).StatusCode);
    }

    private sealed class FailAdminCommit : DbTransactionInterceptor
    {
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default) =>
            eventData.Context!.ChangeTracker.Entries<AuditLog>().Any(x => x.Entity.Action == "ACCOUNT_ROLES_CHANGED")
                ? throw new InvalidOperationException("Simulated administrator commit failure.")
                : ValueTask.FromResult(result);
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQL")]
    public async Task Passenger_data_is_not_returned_when_the_access_audit_cannot_be_saved()
    {
        var admin = await CreateAccountAsync("Admin");
        var target = await CreateAccountAsync("Passenger");
        using var failing = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddDbContext<ApplicationDbContext>(options => options.AddInterceptors(new FailAccessAudit()))));
        using var client = Client(admin, failing);
        var response = await client.GetAsync($"/api/v1/admin/passengers/{target.Id}?purpose=Support");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(target.Email, body);
        Assert.DoesNotContain("Administration Passenger", body);
        Assert.Empty(await AuditsAsync(admin.Id, "PASSENGER_VIEWED"));
    }

    private sealed class FailAccessAudit : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            eventData.Context!.ChangeTracker.Entries<AuditLog>().Any(x => x.Entity.Action == "PASSENGER_VIEWED")
                ? throw new InvalidOperationException("Simulated access audit failure.") : ValueTask.FromResult(result);
    }

    [PostgreSqlFact]
    [Trait("Category", "PostgreSQL")]
    public async Task Concurrent_login_and_refresh_cannot_leave_a_session_active_after_an_admin_lock()
    {
        var admin = await CreateAccountAsync("Admin");
        var target = await CreateAccountAsync("Passenger");
        using var management = Client(admin);
        using var login = factory.CreateClient();
        using var refresh = factory.CreateClient();
        var responses = await Task.WhenAll(
            login.PostAsJsonAsync("/api/v1/auth/login", new { email = target.Email, password = target.Password }),
            refresh.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = target.RefreshToken }),
            StatusAsync(management, target.Id, "LOCKED"));
        Assert.Equal(HttpStatusCode.NoContent, responses[2].StatusCode);
        foreach (var response in responses.Take(2))
        {
            Assert.Contains(response.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized });
            if (response.StatusCode != HttpStatusCode.OK) continue;
            var token = (await response.Content.ReadFromJsonAsync<Tokens>())!;
            using var old = Client(target with { AccessToken = token.AccessToken });
            Assert.Equal(HttpStatusCode.Unauthorized, (await old.GetAsync("/api/v1/me")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await old.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = token.RefreshToken })).StatusCode);
        }
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().AuthSessions
            .AnyAsync(x => x.UserId == target.Id && x.RevokedAt == null));
    }

    private async Task<Account> CreateAccountAsync(string role, bool verified = true)
    {
        var email = $"be12-{Guid.NewGuid():N}@example.invalid";
        var password = $"Metro!{Guid.NewGuid():N}Aa1";
        string id;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = verified };
            Assert.True((await users.CreateAsync(user, password)).Succeeded);
            foreach (var name in new[] { "Passenger", role }.Distinct())
            {
                if (!await roles.RoleExistsAsync(name)) Assert.True((await roles.CreateAsync(new IdentityRole(name))).Succeeded);
                Assert.True((await users.AddToRoleAsync(user, name)).Succeeded);
            }
            id = user.Id;
            db.UserProfiles.Add(new UserProfile { UserId = id, FullName = "Administration Passenger", NormalizedFullName = "ADMINISTRATION PASSENGER", BirthDate = new DateOnly(2000, 1, 1), Status = verified ? "ACTIVE" : "PENDING" });
            await db.SaveChangesAsync();
        }
        var tokens = verified ? await LoginAsync(factory, email, password) : new Tokens("", "");
        return new Account(id, email, password, tokens.AccessToken, tokens.RefreshToken);
    }

    private static async Task<Tokens> LoginAsync(WebApplicationFactory<Program> app, string email, string password)
    {
        using var client = app.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!;
    }

    private HttpClient Client(Account account, WebApplicationFactory<Program>? app = null)
    {
        var client = (app ?? factory).CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", account.AccessToken);
        return client;
    }
    private async Task<AuditLog[]> AuditsAsync(string actorId, string action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().AuditLogs.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.ActorUserId == actorId && x.Action == action).ToArrayAsync();
    }
    private static Task<HttpResponseMessage> StatusAsync(HttpClient client, string id, string status) =>
        client.PutAsJsonAsync($"/api/v1/admin/accounts/{id}/status", new { status, reason = "Account administration" });
    private static async Task ProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, body.GetProperty("code").GetString());
    }
    private async Task<string[]> HideOtherAdministratorsAsync(params string[] keep)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var ids = await (from user in db.Users join assignment in db.UserRoles on user.Id equals assignment.UserId
                         join role in db.Roles on assignment.RoleId equals role.Id
                         where role.Name == "Admin" && user.EmailConfirmed && !keep.Contains(user.Id) select user.Id).ToArrayAsync();
        await db.Users.Where(x => ids.Contains(x.Id)).ExecuteUpdateAsync(s => s.SetProperty(x => x.EmailConfirmed, false));
        return ids;
    }
    private async Task RestoreAdministratorsAsync(string[] ids)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Users.Where(x => ids.Contains(x.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.EmailConfirmed, true));
    }
    private sealed record Tokens(string AccessToken, string RefreshToken);
    private sealed record Account(string Id, string Email, string Password, string AccessToken, string RefreshToken);
}
