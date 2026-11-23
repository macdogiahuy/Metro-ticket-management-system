using System.Security.Claims;
using Metro.Application.Abstractions.Auth;

namespace Metro.API.Endpoints;

public static class AdministrationEndpoints
{
    public static IEndpointRouteBuilder MapAdministrationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin").WithTags("Administration").RequireAuthorization("Admin");
        group.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            return await next(context);
        });
        group.MapGet("/accounts", async (ClaimsPrincipal principal, IAdministrationService service, CancellationToken ct,
            string? search = null, string? role = null, string? status = null, int page = 1, int pageSize = 20) =>
            Results.Ok(await service.FindAccountsAsync(Actor(principal), search, role, status, page, pageSize, ct)))
            .WithName("FindAccounts").Produces<AccountPage>();
        group.MapGet("/roles", () => Results.Ok(new AdministrationRole[]
        {
            new("Passenger", "Passenger", ["own_profile", "own_orders", "own_tickets"]),
            new("StationStaff", "Ticket Inspector", ["passenger", "ticket_inspection", "gate_support"]),
            new("Admin", "Administrator", ["passenger", "ticket_inspection", "account_management", "passenger_management", "eligibility_review"]),
        })).WithName("ListAdministrationRoles").Produces<AdministrationRole[]>();
        group.MapPut("/accounts/{userId}/roles", async (string userId, AccountRolesRequest request,
            ClaimsPrincipal principal, IAdministrationService service, CancellationToken ct) =>
        {
            await service.ChangeRolesAsync(Actor(principal), userId, request, ct);
            return Results.NoContent();
        }).WithName("ChangeAccountRoles").Produces(204);
        group.MapPut("/accounts/{userId}/status", async (string userId, AccountStatusRequest request,
            ClaimsPrincipal principal, IAdministrationService service, CancellationToken ct) =>
        {
            await service.ChangeStatusAsync(Actor(principal), userId, request, ct);
            return Results.NoContent();
        }).WithName("ChangeAccountStatus").Produces(204);
        group.MapGet("/passengers/{userId}", async (string userId, ClaimsPrincipal principal,
            IAdministrationService service, CancellationToken ct, string? purpose = null) =>
            Results.Ok(await service.GetPassengerAsync(Actor(principal), userId, purpose, ct)))
            .WithName("GetPassengerAdministration").Produces<PassengerDetails>();
        group.MapPut("/passengers/{userId}", async (string userId, PassengerUpdateRequest request,
            ClaimsPrincipal principal, IAdministrationService service, CancellationToken ct) =>
        {
            await service.UpdatePassengerAsync(Actor(principal), userId, request, ct);
            return Results.NoContent();
        }).WithName("UpdatePassengerAdministration").Produces(204);
        return app;
    }

    private static string Actor(ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub")
        ?? throw new AuthException("UNAUTHENTICATED", 401, "Missing subject claim.");
}
