using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Metro.API.Configuration;

public sealed class OperationsOpenApiFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (operation.Tags.All(t => t.Name != "Service Operations (BE-14)")) return;
        var write = context.ApiDescription.HttpMethod is "POST" or "PUT" or "DELETE";
        operation.Description = "Administrator with active membership in the explicit operatorCode. UTC timestamps; UTC+7 service dates. " +
            "Service mutations and their audit/idempotent response commit together. Suggestions refresh on the existing 15-second worker; no capacity holds or automatic ticket changes.";
        if (write)
        {
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = "Idempotency-Key", In = ParameterLocation.Header, Required = true,
                Schema = new OpenApiSchema { Type = "string", MinLength = 8, MaxLength = 120 },
                Example = new OpenApiString("operations-request-0001"),
                Description = "Keep the key and payload for retries. Different payload with the same actor/operator/action/key returns 409."
            });
        }
        var station = new OpenApiString("11111111-1111-4111-8111-111111111111");
        var reason = new OpenApiString("Đã xác minh thông tin vận hành");
        OpenApiObject? example = operation.OperationId switch
        {
            "RecordTripEta" => new() { ["stationId"] = station,
                ["expectedArrivalAt"] = new OpenApiString("2026-11-24T10:00:00+07:00"),
                ["expectedDepartureAt"] = new OpenApiString("2026-11-24T10:01:00+07:00"), ["reason"] = reason },
            "RecordTripLoad" => new() { ["observedLoadKg"] = new OpenApiDouble(17999.9),
                ["observedAt"] = new OpenApiString("2026-11-24T10:00:00+07:00"), ["reason"] = reason },
            "UpdateTripOperationalState" => new() { ["status"] = new OpenApiString("DELAYED"), ["reason"] = reason },
            "CreateServiceDisruption" => new() { ["title"] = new OpenApiString("Tạm đóng ga"),
                ["description"] = reason, ["effect"] = new OpenApiString("CLOSED"), ["stationId"] = station,
                ["occurredAt"] = new OpenApiString("2026-11-24T10:00:00+07:00") },
            "PublishServiceDisruption" or "ResolveServiceDisruption" => new() { ["confirmed"] = new OpenApiBoolean(true), ["reason"] = reason },
            _ => null
        };
        if (example is not null && operation.RequestBody is not null)
            foreach (var media in operation.RequestBody.Content.Values) media.Example = example;
    }
}
