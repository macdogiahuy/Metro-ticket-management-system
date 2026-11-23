using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Metro.API.Configuration;

public sealed class TicketOpenApiFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (operation.Tags.All(t => t.Name != "Tickets & history")) return;
        operation.Security = new List<OpenApiSecurityRequirement>
        {
            new() { [new OpenApiSecurityScheme { Reference = new() { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }] = Array.Empty<string>() }
        };
        if (operation.OperationId == "GetMyTicketQr")
            operation.Responses["200"].Content["image/png"] = new OpenApiMediaType
            { Schema = new OpenApiSchema { Type = "string", Format = "binary" } };
        foreach (var (status, description) in new[] { (401, "JWT thiếu hoặc không hợp lệ"), (403, "Không có quan hệ giám hộ hợp lệ"),
            (404, "Không tìm thấy hoặc ngoài quyền"), (409, "Không đủ điều kiện / QR chưa sẵn sàng / xung đột"), (503, "Tạm thời chưa cấp được QR; không thanh toán lại") })
            operation.Responses.TryAdd(status.ToString(), new OpenApiResponse { Description = description, Content = new Dictionary<string, OpenApiMediaType>
            { ["application/problem+json"] = new() { Schema = context.SchemaGenerator.GenerateSchema(typeof(ProblemDetails), context.SchemaRepository) } } });
        if (operation.OperationId == "IssueFreeChildTicket")
        {
            operation.Parameters.Add(new OpenApiParameter { Name = "Idempotency-Key", In = ParameterLocation.Header, Required = true,
                Description = "8–120 ký tự ASCII hiển thị. Giữ cùng key và orderVersion khi thử lại; cùng key khác nội dung trả 409.",
                Schema = new() { Type = "string", MinLength = 8, MaxLength = 120 }, Example = new OpenApiString("be09-free-client-request-001") });
            operation.Responses["202"].Description = "Đã xác nhận miễn phí, chờ QR; vé tương lai đợi 00:00 ngày đi UTC+7. Không tạo Payment.";
            operation.Responses["200"].Description = "Đơn đã phát hành trước đó; trả lại cùng vé.";
        }
    }
}
