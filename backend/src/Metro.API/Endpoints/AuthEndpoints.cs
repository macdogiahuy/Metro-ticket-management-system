using System.Security.Claims;
using Metro.Application.Abstractions.Auth;
using FluentValidation;

namespace Metro.API.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth").WithTags("Auth");

        group.MapPost("/register", RegisterAsync)
            .WithName("Register")
            .Produces<RegisterResult>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/verify-email", VerifyEmailAsync)
            .WithName("VerifyEmail")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/login", LoginAsync)
            .WithName("Login")
            .Produces<TokenResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/refresh", RefreshAsync)
            .WithName("Refresh")
            .Produces<TokenResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/logout", LogoutAsync)
            .WithName("Logout")
            .Produces(StatusCodes.Status204NoContent);

        group.MapGet("/me", MeAsync)
            .WithName("AuthMe")
            .RequireAuthorization()
            .Produces<MeResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        // Hidden endpoint used only by contract tests for the 403 policy path.
        if (app is WebApplication web
            && (web.Environment.IsDevelopment() || web.Configuration["METRO_TEST_MODE"] == "1"))
        {
            group.MapGet("/_role-probe", () => Results.Ok(new { ok = true }))
                .RequireAuthorization("StationStaff")
                .ExcludeFromDescription();
        }

        return app;
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequest request, IValidator<RegisterRequest> validator, IAuthService auth, CancellationToken cancellationToken)
    {
        var invalid = await ValidateAsync(validator, request, cancellationToken);
        if (invalid is not null) return invalid;
        return Results.Created($"/api/v1/auth/me", await auth.RegisterAsync(request, cancellationToken));
    }

    private static async Task<IResult> VerifyEmailAsync(
        VerifyEmailRequest request, IValidator<VerifyEmailRequest> validator, IAuthService auth, CancellationToken cancellationToken)
    {
        var invalid = await ValidateAsync(validator, request, cancellationToken);
        if (invalid is not null) return invalid;
        await auth.VerifyEmailAsync(request, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request, IValidator<LoginRequest> validator, IAuthService auth, CancellationToken cancellationToken)
    {
        var invalid = await ValidateAsync(validator, request, cancellationToken);
        if (invalid is not null) return invalid;
        return Results.Ok(await auth.LoginAsync(request, cancellationToken));
    }

    private static async Task<IResult> RefreshAsync(
        RefreshRequest request, IAuthService auth, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.RefreshToken)] = ["Refresh token is required."] });
        return Results.Ok(await auth.RefreshAsync(request, cancellationToken));
    }

    private static async Task<IResult> LogoutAsync(
        LogoutRequest request, IAuthService auth, CancellationToken cancellationToken)
    {
        await auth.LogoutAsync(request, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> MeAsync(ClaimsPrincipal principal, IAuthService auth, CancellationToken cancellationToken)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                     ?? principal.FindFirstValue("sub")
                     ?? throw new AuthException("UNAUTHENTICATED", StatusCodes.Status401Unauthorized, "Missing subject claim.");
        return Results.Ok(await auth.GetMeAsync(userId, cancellationToken));
    }

    private static async Task<IResult?> ValidateAsync<T>(
        IValidator<T> validator, T request, CancellationToken cancellationToken)
    {
        var result = await validator.ValidateAsync(request, cancellationToken);
        if (result.IsValid) return null;

        return Results.ValidationProblem(result.Errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()),
            title: "Validation failed", statusCode: StatusCodes.Status400BadRequest);
    }
}
