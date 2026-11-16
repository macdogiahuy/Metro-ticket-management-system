using FluentValidation.AspNetCore;
using Metro.API.Configuration;
using Metro.API.Endpoints;
using Metro.API.Middleware;
using Metro.Application;
using Metro.Infrastructure;

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
app.UseAuthorization();
app.MapControllers();
app.MapHealthEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapDiagnosticsEndpoints();
}

app.Run();
