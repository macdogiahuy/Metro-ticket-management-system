namespace Metro.Application.Abstractions.Auth;

/// <summary>BE-03 contract: six /api/v1/auth/* endpoints.</summary>
public sealed record RegisterRequest(string Email, string Password, string FullName, DateOnly BirthDate);
public sealed record RegisterResult(string UserId, string? DevVerificationCode);
public sealed record VerifyEmailRequest(string Email, string Code);
public sealed record LoginRequest(string Email, string Password);
public sealed record TokenResult(string AccessToken, string RefreshToken, DateTime ExpiresAt);
public sealed record RefreshRequest(string RefreshToken);
public sealed record LogoutRequest(string RefreshToken);
public sealed record MeResult(string UserId, string Email, string FullName, string[] Roles, string ProfileStatus);

public interface IAuthService
{
    Task<RegisterResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken);
    Task VerifyEmailAsync(VerifyEmailRequest request, CancellationToken cancellationToken);
    Task<TokenResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken);
    Task<TokenResult> RefreshAsync(RefreshRequest request, CancellationToken cancellationToken);
    Task LogoutAsync(LogoutRequest request, CancellationToken cancellationToken);
    Task<MeResult> GetMeAsync(string userId, CancellationToken cancellationToken);
}

/// <summary>Stable machine-readable error for ProblemDetails extension "code".</summary>
public sealed class AuthException(string code, int status, string message) : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
}

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "metro-api";
    public string Audience { get; set; } = "metro-web";
    public string SigningKey { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 7;
    /// <summary>Development only: return the email verification code in the register response.</summary>
    public bool ExposeVerificationCode { get; set; }
}
