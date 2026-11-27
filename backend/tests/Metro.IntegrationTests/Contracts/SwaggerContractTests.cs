using Metro.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.Swagger;

namespace Metro.IntegrationTests.Contracts;

[Collection(DatabaseIntegrationCollection.Name)]
public sealed class SwaggerContractTests(MetroWebApplicationFactory factory)
{
    [PostgreSqlFact]
    public void OpenApi_exposes_report_filters_authentication_and_csv_contract()
    {
        var swagger = factory.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");
        foreach (var path in new[] { "/api/v1/reports/revenue", "/api/v1/reports/tickets", "/api/v1/reports/revenue/export" })
        {
            var operation = swagger.Paths[path].Operations[OperationType.Get];
            Assert.NotEmpty(operation.Security);
            foreach (var code in new[] { "400", "401", "403" }) Assert.Contains(code, operation.Responses.Keys);
            foreach (var parameter in new[] { "from", "to" })
                Assert.Contains(operation.Parameters, p => string.Equals(p.Name, parameter, StringComparison.OrdinalIgnoreCase) && p.Required);
        }
        var revenue = swagger.Paths["/api/v1/reports/revenue"].Operations[OperationType.Get];
        foreach (var parameter in new[] { "lineId", "ticketProductId", "paymentStatus" })
            Assert.Contains(revenue.Parameters, p => string.Equals(p.Name, parameter, StringComparison.OrdinalIgnoreCase));
        var revenueSchema = swagger.Components.Schemas[revenue.Responses["200"].Content["application/json"].Schema.Reference.Id];
        foreach (var field in new[] { "from", "to", "timezone", "dataAsOf", "totals", "rows" })
            Assert.Contains(field, revenueSchema.Properties.Keys);
        var tickets = swagger.Paths["/api/v1/reports/tickets"].Operations[OperationType.Get];
        foreach (var parameter in new[] { "lineId", "ticketProductId", "ticketStatus", "issuanceStatus", "page", "pageSize" })
            Assert.Contains(tickets.Parameters, p => string.Equals(p.Name, parameter, StringComparison.OrdinalIgnoreCase));
        var export = swagger.Paths["/api/v1/reports/revenue/export"].Operations[OperationType.Get];
        Assert.Contains(export.Parameters, p => string.Equals(p.Name, "purpose", StringComparison.OrdinalIgnoreCase) && p.Required);
        Assert.Contains("text/csv", export.Responses["200"].Content.Keys);
        Assert.Contains("422", export.Responses.Keys);
    }

    [PostgreSqlFact]
    public void OpenApi_contains_current_health_and_auth_contracts()
    {
        var swagger = factory.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");

        Assert.Contains("/health", swagger.Paths.Keys);
        Assert.Contains("/health/live", swagger.Paths.Keys);
        Assert.Contains("/api/v1/auth/register", swagger.Paths.Keys);
        Assert.Contains("/api/v1/auth/login", swagger.Paths.Keys);
        Assert.Contains("/api/v1/auth/me", swagger.Paths.Keys);
        Assert.Contains("/api/v1/lines", swagger.Paths.Keys);
        Assert.Contains("/api/v1/stations", swagger.Paths.Keys);
        Assert.Contains("/api/v1/journeys", swagger.Paths.Keys);
        Assert.Contains("/api/v1/fares/quote", swagger.Paths.Keys);
        Assert.Contains("/api/v1/admin/products", swagger.Paths.Keys);
        Assert.Contains("/api/v1/admin/products/{id}", swagger.Paths.Keys);
        Assert.Contains("/api/v1/admin/fares", swagger.Paths.Keys);
        Assert.Contains("/api/v1/admin/fares/{id}", swagger.Paths.Keys);
        Assert.Contains("/api/v1/admin/fares/{id}/entries", swagger.Paths.Keys);
        Assert.Contains("/api/v1/admin/fares/{id}/approve", swagger.Paths.Keys);
        Assert.Contains("/api/v1/admin/fares/{id}/publish", swagger.Paths.Keys);
        Assert.Contains("/api/v1/admin/eligibility-policies", swagger.Paths.Keys);
        Assert.Contains("/api/v1/admin/eligibility-policies/{id}/approve", swagger.Paths.Keys);
        Assert.Contains("/api/v1/admin/eligibility-policies/{id}/publish", swagger.Paths.Keys);
        Assert.Contains("/api/v1/admin/lines", swagger.Paths.Keys);
        Assert.Contains("/api/v1/admin/lines/{id}", swagger.Paths.Keys);
        Assert.Contains("/api/v1/admin/lines/{id}/stations", swagger.Paths.Keys);
        Assert.Contains("/api/v1/admin/stations", swagger.Paths.Keys);
        Assert.Contains("/api/v1/admin/stations/{id}", swagger.Paths.Keys);
        Assert.Contains("/api/v1/service-status", swagger.Paths.Keys);
        Assert.Contains("/api/v1/payments", swagger.Paths.Keys);
        Assert.Contains("/api/v1/payments/{id}/retry", swagger.Paths.Keys);
        Assert.Contains("/api/v1/webhooks/momo", swagger.Paths.Keys);
        Assert.Contains("/api/v1/reports/revenue", swagger.Paths.Keys);
        Assert.Contains("/api/v1/reports/tickets", swagger.Paths.Keys);
        Assert.Contains("/api/v1/reports/revenue/export", swagger.Paths.Keys);
        Assert.DoesNotContain("/api/v1/auth/_role-probe", swagger.Paths.Keys);
        foreach (var path in new[] { "/api/v1/tickets", "/api/v1/tickets/{id}", "/api/v1/tickets/{id}/qr", "/api/v1/orders", "/api/v1/payments", "/api/v1/orders/{id}/issue-free" })
            Assert.Contains(path, swagger.Paths.Keys);
        var free = swagger.Paths["/api/v1/orders/{id}/issue-free"].Operations[Microsoft.OpenApi.Models.OperationType.Post];
        Assert.Contains(free.Parameters, p => p.Name == "Idempotency-Key" && p.Required);
        Assert.Contains("202", free.Responses.Keys);
        Assert.NotEmpty(free.Security);
        var booking = swagger.Paths["/api/v1/orders"].Operations[OperationType.Post];
        Assert.Contains(booking.Parameters, p => p.Name == "Idempotency-Key" && p.Required);
        Assert.NotEmpty(booking.Security);
        Assert.Contains("/api/v1/orders/{id}/confirm", swagger.Paths.Keys);
        Assert.Contains("/api/v1/orders/{id}/consent", swagger.Paths.Keys);
        Assert.Contains("/api/v1/orders/{id}/suggestion", swagger.Paths.Keys);
        Assert.NotEmpty(swagger.Paths["/api/v1/reports/revenue"].Operations[OperationType.Get].Security);
        var qr = swagger.Paths["/api/v1/tickets/{id}/qr"].Operations[Microsoft.OpenApi.Models.OperationType.Get];
        Assert.Contains("image/png", qr.Responses["200"].Content.Keys);
    }

    [PostgreSqlFact]
    public void OpenApi_exposes_profile_schema_and_private_owner_and_admin_review_routes()
    {
        var swagger = factory.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");
        foreach (var path in new[]
        {
            "/api/v1/me", "/api/v1/me/eligibility", "/api/v1/me/eligibility/{id}",
            "/api/v1/me/eligibility/{id}/evidence", "/api/v1/eligibility",
            "/api/v1/eligibility/{id}", "/api/v1/eligibility/{id}/evidence",
            "/api/v1/eligibility/{id}/decision", "/api/v1/eligibility/{id}/hold"
        }) Assert.Contains(path, swagger.Paths.Keys);
        var responseSchema = swagger.Paths["/api/v1/me"].Operations[OperationType.Get]
            .Responses["200"].Content["application/json"].Schema.Reference.Id;
        var profile = swagger.Components.Schemas[responseSchema];
        Assert.Contains("fullName", profile.Properties.Keys);
        Assert.Contains("birthDate", profile.Properties.Keys);
        Assert.Contains("phoneNumber", profile.Properties.Keys);
        Assert.Contains("identityFieldsLocked", profile.Properties.Keys);
        Assert.DoesNotContain("citizenIdCiphertext", profile.Properties.Keys);
        Assert.DoesNotContain("bankAccountCiphertext", profile.Properties.Keys);
    }

    [PostgreSqlFact]
    public void OpenApi_exposes_admin_contracts_without_private_document_or_identity_fields()
    {
        var swagger = factory.Services.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");
        foreach (var path in new[] { "/api/v1/admin/accounts", "/api/v1/admin/roles",
            "/api/v1/admin/accounts/{userId}/roles", "/api/v1/admin/accounts/{userId}/status", "/api/v1/admin/passengers/{userId}" })
        {
            Assert.Contains(path, swagger.Paths.Keys);
            Assert.All(swagger.Paths[path].Operations.Values, operation => Assert.NotEmpty(operation.Security));
        }
        var details = swagger.Components.Schemas["PassengerDetails"];
        Assert.Contains("citizenId", details.Properties.Keys);
        Assert.DoesNotContain("citizenIdCiphertext", details.Properties.Keys);
        Assert.DoesNotContain("bankAccountCiphertext", details.Properties.Keys);
        var eligibility = swagger.Components.Schemas["PassengerEligibility"];
        Assert.DoesNotContain("evidenceObjectKey", eligibility.Properties.Keys);
    }
}
