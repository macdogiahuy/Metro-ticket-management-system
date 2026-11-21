using System.Security.Claims;
using Metro.Application.Abstractions.Fare;

namespace Metro.API.Endpoints;

public static class FareEndpoints
{
    public static IEndpointRouteBuilder MapFareEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/fares").WithTags("Fare").AllowAnonymous();

        group.MapGet("/quote", (
                Guid? ticketProductId,
                string? productCode,
                Guid? originStationId,
                Guid? destinationStationId,
                DateOnly? travelDate,
                string? eligibilityGroup,
                string? paymentChannel,
                DateOnly? birthDate,
                Guid? farePolicyVersionId,
                string? scopeKey,
                string? operatorCode,
                ClaimsPrincipal principal,
                IFareQuoteService fares,
                CancellationToken ct) =>
            {
                var userId = principal.Identity?.IsAuthenticated == true
                    ? principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub")
                    : null;

                var req = new FareQuoteRequest(
                    ticketProductId, productCode, originStationId, destinationStationId,
                    travelDate, eligibilityGroup, paymentChannel, birthDate,
                    farePolicyVersionId, scopeKey, operatorCode);
                return fares.QuoteAsync(req, userId, ct);
            })
            .WithName("GetFareQuote")
            .WithSummary("UC09 — Tra cứu giá vé theo loại vé, hành trình, phiên bản giá và đối tượng ưu đãi (BR-017, BR-019, BR-021, BR-071)")
            .Produces<FareQuoteResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return app;
    }
}
