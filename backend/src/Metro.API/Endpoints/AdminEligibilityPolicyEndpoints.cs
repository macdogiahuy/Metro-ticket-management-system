using System.Security.Claims;
using Metro.API.Filters;
using Metro.Application.Abstractions.Fare;

namespace Metro.API.Endpoints;

public static class AdminEligibilityPolicyEndpoints
{
    public static IEndpointRouteBuilder MapAdminEligibilityPolicyEndpoints(this IEndpointRouteBuilder app)
    {
        var policies = app.MapGroup("/api/v1/admin/eligibility-policies")
            .WithTags("Admin — Eligibility Policies")
            .RequireAuthorization("Admin");

        policies.MapGet("", (string? status, ClaimsPrincipal p, IAdminEligibilityPolicyService s, CancellationToken ct) =>
                s.ListAsync(status, UserId(p), ct))
            .WithName("ListAdminEligibilityPolicies")
            .WithSummary("UC25 — Danh sách chính sách ưu đãi theo phiên bản")
            .Produces<IReadOnlyList<AdminEligibilityPolicyResponse>>(StatusCodes.Status200OK);

        policies.MapPost("", async (CreateEligibilityPolicyRequest req, ClaimsPrincipal p, IAdminEligibilityPolicyService s, CancellationToken ct) =>
            {
                var created = await s.CreateAsync(req, UserId(p), ct);
                return Results.Created($"/api/v1/admin/eligibility-policies/{created.Id}", created);
            })
            .AddEndpointFilter<IdempotencyEndpointFilter>()
            .WithName("CreateAdminEligibilityPolicy")
            .WithSummary("UC25 — Tạo chính sách ưu đãi DRAFT (chỉ STUDENT, ELDERLY, CHILD_UNDER_6; ghi audit)")
            .Produces<AdminEligibilityPolicyResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status409Conflict);

        policies.MapPost("/{id:guid}/approve", async (Guid id, FarePolicyDecisionRequest req, ClaimsPrincipal p, IAdminEligibilityPolicyService s, CancellationToken ct) =>
                Results.Ok(await s.ApproveAsync(id, req, UserId(p), ct)))
            .AddEndpointFilter<IdempotencyEndpointFilter>()
            .WithName("ApproveAdminEligibilityPolicy")
            .WithSummary("UC25 — Phê duyệt chính sách ưu đãi (maker-checker, ghi audit)")
            .Produces<AdminEligibilityPolicyResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status409Conflict);

        policies.MapPost("/{id:guid}/publish", async (Guid id, FarePolicyDecisionRequest req, ClaimsPrincipal p, IAdminEligibilityPolicyService s, CancellationToken ct) =>
                Results.Ok(await s.PublishAsync(id, req, UserId(p), ct)))
            .AddEndpointFilter<IdempotencyEndpointFilter>()
            .WithName("PublishAdminEligibilityPolicy")
            .WithSummary("UC25 — Công bố chính sách ưu đãi (ghi audit)")
            .Produces<AdminEligibilityPolicyResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    private static string UserId(ClaimsPrincipal principal) =>
        principal.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? principal.FindFirstValue("sub")
        ?? throw new FareException("UNAUTHENTICATED", StatusCodes.Status401Unauthorized, "Missing subject claim.");
}
