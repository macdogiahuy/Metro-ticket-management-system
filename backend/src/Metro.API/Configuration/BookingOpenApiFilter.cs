using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Metro.API.Configuration;

/// <summary>BE-07 examples use the same contracts as Checkout and never imply a reserved seat.</summary>
public sealed class BookingOpenApiFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (operation.Tags.All(t => t.Name != "Booking")) return;
        operation.Description = "One beneficiary and one product per order. Times use UTC+7 business dates. " +
            "Keep Idempotency-Key for the same write; changed content needs a new key. " +
            "A trip suggestion does not reserve capacity. Free child Checkout requires an approved guardian and an eligible paid guardian ticket.";
        foreach (var header in operation.Parameters.Where(p => p.Name == "Idempotency-Key"))
        {
            header.Schema.MinLength = 8;
            header.Schema.MaxLength = 120;
            header.Example = new OpenApiString("booking-request-0001");
        }
        var selection = new OpenApiObject
        {
            ["ticketProductId"] = new OpenApiString("11111111-1111-4111-8111-111111111111"),
            ["eligibilityGroup"] = new OpenApiString("ORDINARY")
        };
        var example = operation.OperationId switch
        {
            "CreateBooking" => selection,
            "ReviseBooking" => new OpenApiObject { ["orderVersion"] = new OpenApiInteger(1), ["selection"] = selection },
            "ConfirmBooking" => new OpenApiObject { ["orderVersion"] = new OpenApiInteger(1), ["expectedTotalMinor"] = new OpenApiLong(30000) },
            "RespondToBookingConsent" => new OpenApiObject { ["orderVersion"] = new OpenApiInteger(1),
                ["consentId"] = new OpenApiString("22222222-2222-4222-8222-222222222222"), ["decision"] = new OpenApiString("ACCEPT") },
            "CancelOrder" => new OpenApiObject { ["orderVersion"] = new OpenApiInteger(1), ["reason"] = new OpenApiString("BUYER_CANCELLED") },
            _ => null
        };
        if (example is not null && operation.RequestBody is not null)
            foreach (var media in operation.RequestBody.Content.Values) media.Example = example;
        foreach (var (status, code) in new[] { ("400", "INVALID_IDEMPOTENCY_KEY"), ("403", "BOOKING_FORBIDDEN"),
            ("404", "BOOKING_NOT_FOUND"), ("409", "ORDER_VERSION_CONFLICT"), ("422", "TRAVEL_DATE_OUT_OF_RANGE") })
            if (operation.Responses.TryGetValue(status, out var response))
                foreach (var media in response.Content.Values)
                    media.Example = new OpenApiObject { ["status"] = new OpenApiInteger(int.Parse(status)),
                        ["title"] = new OpenApiString("Booking request failed"), ["code"] = new OpenApiString(code) };
        foreach (var status in new[] { "200", "201" })
            if (operation.Responses.TryGetValue(status, out var response))
                foreach (var media in response.Content.Values)
                    media.Example = operation.OperationId == "GetBookingSuggestion"
                        ? new OpenApiObject { ["orderVersion"] = new OpenApiInteger(1), ["status"] = new OpenApiString("NO_TRIP"),
                            ["holdsCapacity"] = new OpenApiBoolean(false), ["notice"] = new OpenApiString("Gợi ý chuyến — không giữ chỗ") }
                        : operation.OperationId == "ListPendingBookingConsents" ? new OpenApiArray()
                        : new OpenApiObject { ["id"] = new OpenApiString("33333333-3333-4333-8333-333333333333"),
                            ["orderVersion"] = new OpenApiInteger(1), ["status"] = new OpenApiString("DRAFT"),
                            ["subtotalMinor"] = new OpenApiLong(30000), ["discountMinor"] = new OpenApiLong(0),
                            ["totalMinor"] = new OpenApiLong(30000), ["currency"] = new OpenApiString("VND") };
    }
}
