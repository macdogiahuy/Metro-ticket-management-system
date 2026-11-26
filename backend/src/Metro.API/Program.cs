using System.Security.Cryptography;
using System.Text;
using FluentValidation.AspNetCore;
using Metro.API.Configuration;
using Metro.API.Endpoints;
using Metro.API.Middleware;
using Metro.Application;
using Metro.Application.Abstractions.Auth;
using Metro.Infrastructure;
using Metro.Infrastructure.Payments;
using Metro.Infrastructure.Identity;
using Metro.Application.Abstractions.Payment;
using Metro.Application.Abstractions.Ticketing;
using Metro.Infrastructure.Ticketing;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// neon deploy writes DATABASE_URL and the AWS_* storage variables into .env.local (or .env).
// Only read that local file in Development; deployed environments get real env vars.
if (builder.Environment.IsDevelopment())
{
    EnvFileConfiguration.LoadNearest(builder.Configuration);
}

builder.Services.AddControllers();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// DoD BE-01: mọi lỗi (exception chưa xử lý và status code 4xx trống thân phản hồi)
// được chuẩn hoá về RFC 9457 ProblemDetails.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// DoD BE-01: validation pipeline — FluentValidation validators trong Metro.Application
// chạy trước khi request vào controller, trả 400 ValidationProblemDetails khi fail.
builder.Services.AddFluentValidationAutoValidation();

// BE-03: Identity + JWT. Signing key is required in Production; Development uses
// an ephemeral key so the API stays runnable without committing secrets.
var jwtSection = builder.Configuration.GetSection("Jwt");
var signingKey = jwtSection["SigningKey"];
if (string.IsNullOrWhiteSpace(signingKey))
{
    if (builder.Environment.IsProduction())
        throw new InvalidOperationException("Jwt:SigningKey must be configured (env Jwt__SigningKey).");
    signingKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
}

var jwtOptions = new JwtOptions
{
    Issuer = jwtSection["Issuer"] ?? "metro-api",
    Audience = jwtSection["Audience"] ?? "metro-web",
    SigningKey = signingKey,
    AccessTokenMinutes = int.TryParse(jwtSection["AccessTokenMinutes"], out var accessMinutes) ? accessMinutes : 15,
    RefreshTokenDays = int.TryParse(jwtSection["RefreshTokenDays"], out var refreshDays) ? refreshDays : 7,
    ExposeVerificationCode = builder.Environment.IsDevelopment() || builder.Configuration["METRO_TEST_MODE"] == "1",
};
builder.Services.AddSingleton(Options.Create(jwtOptions));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.EventsType = typeof(AccountAccess);
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Passenger", policy => policy.RequireRole("Passenger"));
    options.AddPolicy("StationStaff", policy => policy.RequireRole("StationStaff"));
    options.AddPolicy("Admin", policy => policy.RequireRole("Admin"));
    options.AddPolicy("StaffOrAdmin", policy => policy.RequireRole("Admin", "StationStaff"));
});
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, AdministrationAuthorizationHandler>();

builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddScoped<IRefundService, RefundService>();
builder.Services.AddScoped<ITicketChangeService, TicketChangeService>();
builder.Services.AddHttpClient<IPaymentGateway, MoMoPaymentGateway>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options => options.ConfigureMetroApi());

// DoD BE-01: CORS cho frontend; danh sách origin đọc từ Frontend:AllowedOrigins
// (env Frontend__AllowedOrigins, phân tách bằng dấu phẩy), mặc định localhost:5173.
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        var configured = builder.Configuration["Frontend:AllowedOrigins"];
        var origins = configured?
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (origins is { Length: > 0 })
        {
            policy.WithOrigins(origins);
        }
        else
        {
            policy.WithOrigins("http://localhost:5173");
        }

        policy.AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

app.UseMiddleware<RequestLoggingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// CORS trước exception handler để response lỗi vẫn mang CORS header cho browser.
app.UseCors("Frontend");
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapAuthEndpoints();
app.MapProfileEndpoints();
app.MapAdministrationEndpoints();
app.MapCatalogEndpoints();
app.MapFareEndpoints();
app.MapAdminProductEndpoints();
app.MapAdminFareEndpoints();
app.MapAdminEligibilityPolicyEndpoints();
app.MapAdminNetworkEndpoints();
app.MapAdminScheduleEndpoints();
app.MapAdminTripEndpoints();
app.MapAdminTripLoadEndpoints();
app.MapPaymentEndpoints();
app.MapTicketEndpoints();
app.MapGateEndpoints();
app.MapBookingEndpoints();
app.MapOperationsEndpoints();
app.MapRefundEndpoints();
app.MapTicketChangeEndpoints();
app.MapReportEndpoints();
app.MapHealthEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapDiagnosticsEndpoints();
}

app.Run();

// Exposed for WebApplicationFactory-based integration tests.
public partial class Program { }
