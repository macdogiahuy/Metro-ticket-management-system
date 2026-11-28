using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Metro.API.Configuration;

/// <summary>OpenAPI Idempotency-Key header &amp; request examples for BE-13 admin network/operations endpoints.</summary>
public sealed class NetworkAdminOpenApiFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (!operation.Tags.Any(t => t.Name.Contains("(BE-13)", StringComparison.Ordinal))) return;
        var method = context.ApiDescription.HttpMethod;
        if (method is "POST" or "PUT" or "DELETE" && operation.Parameters.All(p => !string.Equals(p.Name, "Idempotency-Key", StringComparison.OrdinalIgnoreCase)))
        {
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = "Idempotency-Key",
                In = ParameterLocation.Header,
                Required = false,
                Description = "Optional 1..128 char idempotency key for safe write retries; conflicting payload returns 409 IDEMPOTENCY_KEY_CONFLICT.",
                Schema = new OpenApiSchema { Type = "string", MinLength = 1, MaxLength = 128 },
                Example = new OpenApiString("be13-idem-0001"),
            });
        }

        var example = operation.OperationId switch
        {
            "CreateAdminLine" => new OpenApiObject { ["code"] = new OpenApiString("T01"), ["name"] = new OpenApiString("Tuyen 01"), ["status"] = new OpenApiString("ACTIVE"), ["reason"] = new OpenApiString("Initialize line") },
            "CreateAdminStation" => new OpenApiObject { ["code"] = new OpenApiString("ST-C"), ["name"] = new OpenApiString("Ga C"), ["stationType"] = new OpenApiString("INTERCHANGE"), ["minTransferSeconds"] = new OpenApiInteger(300), ["status"] = new OpenApiString("ACTIVE") },
            "CreateAdminLoadProfile" => new OpenApiObject { ["code"] = new OpenApiString("LP-DT01"), ["maxLoadKg"] = new OpenApiDouble(18000.0), ["effectiveFrom"] = new OpenApiString("2026-11-26T00:00:00Z"), ["reason"] = new OpenApiString("3 cars x 6000 kg") },
            "RecordAdminTripLoadObservation" => new OpenApiObject { ["observedLoadKg"] = new OpenApiDouble(17999.9), ["note"] = new OpenApiString("Manual kg entry") },
            "RecordAdminTripEtaUpdate" => new OpenApiObject { ["expectedArrivalAt"] = new OpenApiString("2026-11-26T08:10:00Z"), ["expectedDepartureAt"] = new OpenApiString("2026-11-26T08:11:00Z"), ["note"] = new OpenApiString("Approaching platform 1") },
            "CreateAdminNotice" => new OpenApiObject { ["code"] = new OpenApiString("NTC-001"), ["audience"] = new OpenApiString("PUBLIC"), ["title"] = new OpenApiString("Thong bao lich tau"), ["content"] = new OpenApiString("Gian cach 10 phut/chuyen."), ["emergencyTemplate"] = new OpenApiBoolean(false), ["validFrom"] = new OpenApiString("2026-11-26T00:00:00Z"), ["validTo"] = new OpenApiString("2026-11-27T00:00:00Z") },
            _ => null,
        };
        if (example is not null && operation.RequestBody is not null)
            foreach (var media in operation.RequestBody.Content.Values) media.Example = example;
    }
}
