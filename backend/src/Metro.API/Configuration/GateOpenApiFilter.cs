using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Metro.API.Configuration;

public sealed class GateOpenApiFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (operation.Tags.All(t => t.Name != "Gate validation (BE-10)")) return;
        if(operation.OperationId is "RequestGateQrVerification" or "VerifyGateQrSupport" or "GetGateQrSupportResult" or "GetVerifiedGateQrImage")
        {
            operation.Description="UC18 / BR-046 / SRS 8.2. Active StationStaff/Admin and operator membership required. " +
                "Verify the ticket holder's exact normalized name and birth date; an email code is bound to the ticket, operationId, action and staff session. " +
                "Six digits, five minutes, one use, five failures; shared email quota: at least 60 seconds between codes, at most three across purposes per rolling 15 minutes. " +
                "Codes are never returned in HTTP, even in Development. Document fallback requires noEmailAccess, documentMatchesProfile and an evidence reference. " +
                "Use a new requestId per verification attempt and keep operationId fixed. Retry an unconfirmed request with exactly the same id/body. " +
                "READY authorizes the verified staff session to fetch a private PNG, not to grant gate passage. Replacement does not extend validity or close/reopen a journey. " +
                "Email ACCEPTED means SMTP acceptance, not inbox delivery; GET the receipt to see current queue state.";
            var header=operation.Parameters.SingleOrDefault(p=>p.In==ParameterLocation.Header && p.Name=="Idempotency-Key");
            if(header is not null)
            {
                header.Required=true;header.Description="UUID D format, equal to requestId; operationId identifies the pending support action and must be a different UUID.";
                header.Schema=new(){Type="string",Format="uuid"};
            }
            if(operation.RequestBody?.Content.TryGetValue("application/json",out var body) is true)
            {
                var example=new OpenApiObject
                {
                    ["requestId"]=new OpenApiString("10000000-0000-0000-0000-000000000020"),
                    ["operationId"]=new OpenApiString("10000000-0000-0000-0000-000000000021"),
                    ["action"]=new OpenApiString("REPLACE"),["fullName"]=new OpenApiString("Tên hành khách minh họa"),
                    ["birthDate"]=new OpenApiString("1990-01-01"),["reason"]=new OpenApiString("Khách yêu cầu thay QR bị mất")
                };
                if(operation.OperationId=="VerifyGateQrSupport")
                {
                    example["verificationMethod"]=new OpenApiString("EMAIL");
                    example["challengeId"]=new OpenApiString("10000000-0000-0000-0000-000000000022");
                    example["emailCode"]=new OpenApiString("<six digits received by the ticket holder>");
                }
                body.Example=example;
            }
            if(operation.OperationId=="GetVerifiedGateQrImage")
                operation.Responses["200"].Content["image/png"]=new OpenApiMediaType{Schema=new(){Type="string",Format="binary"}};
            return;
        }
        operation.Description = "Requires an active StationStaff or Admin session and active staff/admin membership in the explicit operatorCode. " +
            "Never cache QR or gate decisions for offline use. Allow is returned only after commit; Deny never grants passage. " +
            "202/Pending means unconfirmed: retain the requestId and unchanged body, query the result or replay the original request. " +
            "REVIEW in stored AFC correction history is not Pending. Exit needs no entry trip or load measurement. " +
            "Business denials use HTTP 200 with decision=Deny and reasonCode; invalid input/permission/conflict use ProblemDetails.";
        if (operation.OperationId is "ScanGateTicket" or "ResolveGateException")
        {
            var key = operation.Parameters.SingleOrDefault(p => p.In == ParameterLocation.Header && p.Name == "Idempotency-Key");
            if (key is not null)
            {
                key.Required = true; key.Description = "UUID in D format; must equal body.requestId. Same ID with changed body returns 409 IDEMPOTENCY_KEY_CONFLICT.";
                key.Schema = new() { Type = "string", Format = "uuid" }; key.Example = new OpenApiString("10000000-0000-0000-0000-000000000010");
            }
            if (operation.RequestBody?.Content.TryGetValue("application/json", out var body) is true)
                body.Example = operation.OperationId == "ScanGateTicket" ? new OpenApiObject
                {
                    ["requestId"] = new OpenApiString("10000000-0000-0000-0000-000000000010"), ["action"] = new OpenApiString("ENTRY"),
                    ["stationId"] = new OpenApiString("20000000-0000-0000-0000-000000000001"),
                    ["plannedTripId"] = new OpenApiString("30000000-0000-0000-0000-000000000001"), ["direction"] = new OpenApiInteger(0),
                    ["qrTokens"] = new OpenApiArray { new OpenApiString("<current QR payload from BE-09>") },
                    ["deviceCode"] = new OpenApiString("STAFF_BROWSER"), ["pairPresentConfirmed"] = new OpenApiBoolean(false)
                } : new OpenApiObject
                {
                    ["requestId"] = new OpenApiString("10000000-0000-0000-0000-000000000011"),
                    ["sourceRequestId"] = new OpenApiString("10000000-0000-0000-0000-000000000010"),
                    ["action"] = new OpenApiString("CLOSE_JOURNEY"), ["stationId"] = new OpenApiString("20000000-0000-0000-0000-000000000002"),
                    ["reason"] = new OpenApiString("Verified actual exit station"), ["evidenceReference"] = new OpenApiString("staff-case-reference"),
                    ["identityVerified"] = new OpenApiBoolean(true), ["pairPresentConfirmed"] = new OpenApiBoolean(false)
                };
            operation.Responses["200"].Description = "Committed Allow or Deny, with requestId/reasonCode and immutable AFC event references.";
            operation.Responses["202"].Description = "Pending: no confirmed permission. Recover/replay the same request before creating another.";
        }
        if (operation.OperationId is "ScanGateTicket" or "GetGateScanResult" or "ResolveGateException")
            foreach (var status in new[] { "200", "202" })
                if (operation.Responses.TryGetValue(status, out var response) && response.Content.TryGetValue("application/json", out var media))
                    media.Example = new OpenApiObject
                    {
                        ["requestId"] = new OpenApiString("10000000-0000-0000-0000-000000000010"),
                        ["decision"] = new OpenApiString(status == "202" ? "Pending" : "Allow"),
                        ["reasonCode"] = new OpenApiString(status == "202" ? "GATE_RESULT_UNCONFIRMED" : "PASSAGE_RECORDED"),
                        ["processedAt"] = status == "202" ? new OpenApiNull() : new OpenApiString("2026-11-25T01:00:00Z"),
                        ["passages"] = new OpenApiArray()
                    };
    }
}
