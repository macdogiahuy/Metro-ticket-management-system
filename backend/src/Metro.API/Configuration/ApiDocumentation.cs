using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Metro.API.Configuration;

/// <summary>
/// Cấu hình Swagger/OpenAPI cho Metro API (DoD BE-01): thông tin tài liệu,
/// kèm ví dụ response ProblemDetails 400/500 cho mọi endpoint.
/// </summary>
public static class ApiDocumentation
{
    public static void ConfigureMetroApi(this SwaggerGenOptions options)
    {
        options.SwaggerDoc("v1", new OpenApiInfo
        {
            Version = "v1",
            Title = "Metro Ticket Management API",
            Description = "API hệ thống bán và quản lý vé metro (Đồ án nhóm 6). " +
                          "Mọi lỗi HTTP >= 400 trả về RFC 9457 ProblemDetails (application/problem+json).",
            Contact = new OpenApiContact
            {
                Name = "Nhóm 6 — Metro Ticket Management System",
            },
        });

        options.OperationFilter<ProblemDetailsOperationFilter>();

        var xmlFile = $"{typeof(ApiDocumentation).Assembly.GetName().Name}.xml";
        var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
        if (File.Exists(xmlPath))
        {
            options.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);
        }
    }

    /// <summary>
    /// Gắn schema + example 400 (ValidationProblemDetails) và 500 (ProblemDetails)
    /// vào mọi operation chưa khai báo response tương ứng.
    /// </summary>
    private sealed class ProblemDetailsOperationFilter : IOperationFilter
    {
        private const string Validation400Example =
            """
            {
              "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
              "title": "One or more validation errors occurred.",
              "status": 400,
              "errors": { "Request.ProductCode": ["ProductCode is required."] },
              "traceId": "00-8f4c0f0a1b2c3d4e5f60718293a4b5c6-0000000000000000-00"
            }
            """;

        private const string NotFound404Example =
            """
            {
              "type": "https://tools.ietf.org/html/rfc9110#section-15.5.5",
              "title": "Resource not found",
              "status": 404,
              "detail": "Station or line was not found.",
              "code": "STATION_NOT_FOUND"
            }
            """;

        private const string Unprocessable422Example =
            """
            {
              "type": "https://tools.ietf.org/html/rfc9110#section-15.5.21",
              "title": "Route unavailable",
              "status": 422,
              "detail": "No serving metro route is currently available between the selected stations.",
              "code": "PATH_UNAVAILABLE"
            }
            """;

        private const string ServerError500Example =
            """
            {
              "type": "https://tools.ietf.org/html/rfc9110#section-15.6.1",
              "title": "An unexpected error occurred",
              "status": 500,
              "traceId": "00-8f4c0f0a1b2c3d4e5f60718293a4b5c6-0000000000000000-00"
            }
            """;

        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            if (!operation.Responses.ContainsKey("400"))
            {
                operation.Responses.Add("400", BuildResponse(context, typeof(ValidationProblemDetails),
                    "Yêu cầu không hợp lệ — validation thất bại.", Validation400Example));
            }

            if (operation.Responses.TryGetValue("404", out var notFound) &&
                notFound.Content.TryGetValue("application/problem+json", out var notFoundMedia) &&
                notFoundMedia.Example is null)
            {
                notFoundMedia.Example = new OpenApiString(NotFound404Example);
            }

            if (operation.Responses.TryGetValue("422", out var unprocessable) &&
                unprocessable.Content.TryGetValue("application/problem+json", out var unprocessableMedia) &&
                unprocessableMedia.Example is null)
            {
                unprocessableMedia.Example = new OpenApiString(Unprocessable422Example);
            }

            if (!operation.Responses.ContainsKey("500"))
            {
                operation.Responses.Add("500", BuildResponse(context, typeof(ProblemDetails),
                    "Lỗi không mong đợi phía server.", ServerError500Example));
            }
        }

        private static OpenApiResponse BuildResponse(OperationFilterContext context, Type schemaType,
            string description, string exampleJson)
        {
            var schema = context.SchemaGenerator.GenerateSchema(schemaType, context.SchemaRepository);

            return new OpenApiResponse
            {
                Description = description,
                Content =
                {
                    ["application/problem+json"] = new OpenApiMediaType
                    {
                        Schema = schema,
                        Example = new OpenApiString(exampleJson),
                    },
                },
            };
        }
    }
}
