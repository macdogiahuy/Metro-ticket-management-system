using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Metro.API.Configuration;

/// <summary>Documents the current BE-15 HTTP contract without changing request handling.</summary>
public sealed class ReportOpenApiFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (operation.OperationId is not ("GetRevenueReport" or "GetTicketReport" or "ExportRevenueReportCsv")) return;
        var isTickets = operation.OperationId == "GetTicketReport";
        var isExport = operation.OperationId == "ExportRevenueReportCsv";
        var shared = "Admin JWT and an active operator account belonging to the authenticated user are required. " +
            "When multiple active memberships exist, the account with the smallest ID selects the operator. " +
            "Dates are inclusive business dates in Asia/Ho_Chi_Minh, at most 31 days. " +
            "All components of one response use a repeatable-read database snapshot. " +
            "dataAsOf is an application timestamp, not the exact database snapshot time. ";
        operation.Description = shared + (isTickets
            ? "Tickets are selected by issuedAt. READY counts as ready; PENDING and NEEDS_SUPPORT count as pending; CANCELLED counts as neither. A missing job is returned as UNAVAILABLE. Totals cover all filtered rows before pagination. freeOrderCount and paidOrderCount count distinct orders represented by filtered tickets, using free_confirmed_at to distinguish free orders. " +
              "lineId selects the latest suggestion for the ticket order/version computed by ticket issuedAt; it is not a reserved or actual journey. Sort is issuedAt descending then ticketId ascending."
            : "Payments are selected by succeeded_at and current status SUCCEEDED, PARTIALLY_REFUNDED, or REFUNDED, including UNRECONCILED. " +
              "Valid paid value is min(amount, frozen order-version total); excess is max(0, amount minus frozen total). " +
              "Successful refunds use refund succeeded_at. Pending refunds are requests created in the range whose current request status is unresolved; they are not a historical end-of-period balance. " +
              "Net cashflow is collected minus successful refunds. paymentStatus filters collection events only, not refunds. " +
              "lineId uses the last suggestion computed at or before payment succeeded_at, not a reserved or actual journey; ticketProductId uses the payment's frozen order version.") +
            (isExport ? " CSV contains one row per business day with activity; output is limited to 10,000 grouped rows (at most 31 under the current date limit). A successful export writes a report job and audit record before returning. Each successful request records a new export; retries are not idempotent." : "");

        foreach (var parameter in operation.Parameters.Where(p => p.In == ParameterLocation.Query))
        {
            var name = parameter.Name.ToLowerInvariant();
            switch (name)
            {
                case "from": case "to":
                    parameter.Required = true;
                    parameter.Description = "Required business date in yyyy-MM-dd, inclusive, Asia/Ho_Chi_Minh. Range may span at most 31 days.";
                    parameter.Example = new OpenApiString(name == "from" ? "2026-11-01" : "2026-11-30");
                    break;
                case "lineid": parameter.Description = "Optional line ID; filters by the historical suggestion, not actual travel."; break;
                case "ticketproductid": parameter.Description = "Optional ticket product ID."; break;
                case "paymentstatus":
                    parameter.Description = isTickets ? "Validated when present but does not filter this endpoint." : "Filters payment collections only; refunds remain unfiltered by payment status. Values include PENDING, SUCCEEDED, FAILED, EXPIRED, CANCELLED, PARTIALLY_REFUNDED, REFUNDED, REVIEW_REQUIRED; only the three collected statuses contribute to collections.";
                    break;
                case "ticketstatus":
                    parameter.Description = isTickets ? "Ticket status: ISSUED, ACTIVE, IN_USE, USED, EXPIRED, REFUND_PENDING, REFUNDED, REVOKED." : "Validated when present but does not filter this endpoint.";
                    break;
                case "issuancestatus":
                    parameter.Description = isTickets ? "Issuance job status: PENDING, READY, NEEDS_SUPPORT, CANCELLED. Missing jobs appear as UNAVAILABLE but cannot be selected by this filter." : "Validated when present but does not filter this endpoint.";
                    break;
                case "page":
                    parameter.Description = isTickets ? "Page number; defaults to 1; permitted range 1–100000." : "Validated when present but does not paginate this endpoint.";
                    if (isTickets) { parameter.Schema.Default = new OpenApiInteger(1); parameter.Schema.Minimum = 1; parameter.Schema.Maximum = 100000; }
                    break;
                case "pagesize":
                    parameter.Description = isTickets ? "Rows per page; defaults to 50; permitted range 1–100." : "Validated when present but does not paginate this endpoint.";
                    if (isTickets) { parameter.Schema.Default = new OpenApiInteger(50); parameter.Schema.Minimum = 1; parameter.Schema.Maximum = 100; }
                    break;
                case "purpose":
                    parameter.Required = isExport;
                    parameter.Description = isExport ? "Required non-sensitive audit purpose; 3–300 characters after trimming. Stored in the report job and audit log." : "Not used by this endpoint.";
                    if (isExport) { parameter.Schema.MaxLength = 300; parameter.Example = new OpenApiString("Monthly reconciliation"); }
                    break;
            }
        }
        if (operation.Responses.TryGetValue("400", out var badRequest))
            badRequest.Description = "Invalid date range, pagination, status or export purpose; ProblemDetails code identifies the failure.";
        if (operation.Responses.TryGetValue("403", out var forbidden))
            forbidden.Description = "Admin role or active operator membership required; missing membership code: REPORT_OPERATOR_FORBIDDEN.";
        if (isExport && operation.Responses.TryGetValue("422", out var tooLarge))
            tooLarge.Description = "REPORT_EXPORT_LIMIT_EXCEEDED: more than 10,000 grouped rows. The 31-day limit currently prevents reaching this branch.";
        if (isExport && operation.Responses.TryGetValue("200", out var csv))
        {
            csv.Description = "UTF-8 CSV containing grouped daily revenue rows; successful exports are audited.";
            csv.Content["text/csv"] = new OpenApiMediaType
            {
                Schema = new OpenApiSchema { Type = "string", Format = "binary" },
            };
        }
    }
}
