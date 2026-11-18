using System.Security.Cryptography;
using System.Text;
using System.IdentityModel.Tokens.Jwt;
using Metro.Application.Abstractions.Auth;
using Metro.Domain.Entities.Identity;
using Metro.Infrastructure.Persistence.Context;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Metro.Infrastructure.Identity;

public sealed class AuthService(
    UserManager<IdentityUser> users,
    RoleManager<IdentityRole> roles,
    ApplicationDbContext db,
    IOptions<JwtOptions> options) : IAuthService
{
    private JwtOptions Config => options.Value;

    public async Task<RegisterResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        if (await users.FindByEmailAsync(email) is not null)
            throw new AuthException("EMAIL_ALREADY_EXISTS", StatusCodes.Status400BadRequest, "Email is already registered.");

        var user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = false };
        var created = await users.CreateAsync(user, request.Password);
        if (!created.Succeeded)
            throw new AuthException("WEAK_PASSWORD", StatusCodes.Status400BadRequest, "Password does not meet the policy.");

        await EnsureRoleAsync("Passenger");
        await users.AddToRoleAsync(user, "Passenger");

        db.UserProfiles.Add(new UserProfile
        {
            UserId = user.Id,
            FullName = request.FullName.Trim(),
            NormalizedFullName = request.FullName.Trim().ToUpperInvariant(),
            BirthDate = request.BirthDate,
        });

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var now = DateTime.UtcNow;
        db.EmailVerificationChallenges.Add(new EmailVerificationChallenge
        {
            UserId = user.Id,
            Purpose = "VERIFY_EMAIL",
            CodeHash = Hash(code),
            CreatedAt = now,
            // SQL constraint: expires_at = created_at + interval '10 minutes'.
            ExpiresAt = now.AddMinutes(10),
        });
        await db.SaveChangesAsync(cancellationToken);

        return new RegisterResult(user.Id, Config.ExposeVerificationCode ? code : null);
    }

    public async Task VerifyEmailAsync(VerifyEmailRequest request, CancellationToken cancellationToken)
    {
        var user = await users.FindByEmailAsync(request.Email.Trim().ToLowerInvariant());
        if (user is null)
            throw new AuthException("USER_NOT_FOUND", StatusCodes.Status404NotFound, "Account not found.");

        var now = DateTime.UtcNow;
        var challenge = await db.EmailVerificationChallenges
            .Where(x => x.UserId == user.Id && x.Purpose == "VERIFY_EMAIL"
                     && x.ConsumedAt == null && x.SupersededAt == null && x.ExpiresAt > now)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (challenge is null)
            throw new AuthException("INVALID_VERIFICATION_CODE", StatusCodes.Status400BadRequest, "Verification code is missing or expired.");

        if (!FixedTimeEquals(challenge.CodeHash, Hash(request.Code.Trim())))
        {
            challenge.FailedAttempts++;
            await db.SaveChangesAsync(cancellationToken);
            throw new AuthException("INVALID_VERIFICATION_CODE", StatusCodes.Status400BadRequest, "Verification code is invalid.");
        }

        challenge.ConsumedAt = now;
        user.EmailConfirmed = true;
        await users.UpdateAsync(user);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<TokenResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var user = await users.FindByEmailAsync(request.Email.Trim().ToLowerInvariant());
        if (user is null || !await users.CheckPasswordAsync(user, request.Password))
            throw new AuthException("INVALID_CREDENTIALS", StatusCodes.Status401Unauthorized, "Invalid email or password.");
        if (!user.EmailConfirmed)
            throw new AuthException("EMAIL_NOT_VERIFIED", StatusCodes.Status403Forbidden, "Email must be verified before login.");

        return await IssueTokensAsync(user, cancellationToken);
    }

    public async Task<TokenResult> RefreshAsync(RefreshRequest request, CancellationToken cancellationToken)
    {
        var hash = Hash(request.RefreshToken);
        var now = DateTime.UtcNow;
        var session = await db.AuthSessions
            .FirstOrDefaultAsync(x => x.RefreshTokenHash == hash && x.RevokedAt == null && x.ExpiresAt > now, cancellationToken);
        if (session is null)
            throw new AuthException("INVALID_REFRESH_TOKEN", StatusCodes.Status401Unauthorized, "Refresh token is invalid or expired.");

        var user = await users.FindByIdAsync(session.UserId);
        if (user is null)
            throw new AuthException("INVALID_REFRESH_TOKEN", StatusCodes.Status401Unauthorized, "Refresh token is invalid or expired.");

        session.RevokedAt = now;
        session.RevocationReason = "ROTATED";
        await db.SaveChangesAsync(cancellationToken);

        return await IssueTokensAsync(user, cancellationToken);
    }

    public async Task LogoutAsync(LogoutRequest request, CancellationToken cancellationToken)
    {
        var hash = Hash(request.RefreshToken);
        var session = await db.AuthSessions.FirstOrDefaultAsync(x => x.RefreshTokenHash == hash && x.RevokedAt == null, cancellationToken);
        if (session is null) return; // idempotent
        session.RevokedAt = DateTime.UtcNow;
        session.RevocationReason = "LOGOUT";
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<MeResult> GetMeAsync(string userId, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(userId)
            ?? throw new AuthException("USER_NOT_FOUND", StatusCodes.Status404NotFound, "Account not found.");
        var profile = await db.UserProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        return new MeResult(user.Id, user.Email ?? string.Empty,
            profile?.FullName ?? string.Empty,
            (await users.GetRolesAsync(user)).ToArray(),
            profile?.Status ?? "PENDING");
    }

    private async Task<TokenResult> IssueTokensAsync(IdentityUser user, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var refreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        db.AuthSessions.Add(new AuthSession
        {
            UserId = user.Id,
            RefreshTokenHash = Hash(refreshToken),
            IssuedAt = now,
            ExpiresAt = now.AddDays(Config.RefreshTokenDays),
        });
        await db.SaveChangesAsync(cancellationToken);

        var expires = now.AddMinutes(Config.AccessTokenMinutes);
        var claims = new List<System.Security.Claims.Claim>
        {
            new(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub, user.Id),
            new(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
        };
        claims.AddRange((await users.GetRolesAsync(user)).Select(role => new System.Security.Claims.Claim(
            System.Security.Claims.ClaimTypes.Role, role)));

        var token = new JwtSecurityToken(
            Config.Issuer, Config.Audience, claims, now, expires,
            new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Config.SigningKey)),
                SecurityAlgorithms.HmacSha256));

        return new TokenResult(new JwtSecurityTokenHandler().WriteToken(token), refreshToken, expires);
    }

    private async Task EnsureRoleAsync(string name)
    {
        if (!await roles.RoleExistsAsync(name))
            await roles.CreateAsync(new IdentityRole(name));
    }

    // SQL check requires ^[0-9a-f]{64}$ for code_hash and refresh_token_hash.
    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static bool FixedTimeEquals(string left, string right) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));
}
