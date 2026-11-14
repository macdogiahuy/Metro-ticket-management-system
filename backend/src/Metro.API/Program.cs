using Metro.API.Configuration;
using Metro.API.Endpoints;
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

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
        policy.WithOrigins("http://localhost:5173")
              .AllowAnyHeader()
              .AllowAnyMethod());
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("Frontend");
app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();
app.MapHealthEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapDiagnosticsEndpoints();
}

app.Run();
