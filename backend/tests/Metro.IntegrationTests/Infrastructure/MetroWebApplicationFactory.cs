using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;

namespace Metro.IntegrationTests.Infrastructure;

public sealed class MetroWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _signingKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));

    public TestLogSink TestLogs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var postgres = PostgreSqlTestConfiguration.LoadRequired();

        builder.UseEnvironment("Testing");
        builder.UseSetting("DATABASE_URL", postgres.ConnectionString);
        builder.UseSetting("Redis:ConnectionString", Environment.GetEnvironmentVariable("METRO_TEST_REDIS_CONNECTION_STRING") ?? string.Empty);
        builder.UseSetting("Jwt:SigningKey", _signingKey);
        builder.UseSetting("Jwt:Issuer", "metro-api");
        builder.UseSetting("Jwt:Audience", "metro-web");
        builder.UseSetting("TicketQr:WorkerEnabled", "false");
        builder.UseSetting("TicketQr:ActiveKeyId", "test-v1");
        builder.UseSetting("TicketQr:Keys:test-v1", _signingKey);
        builder.UseSetting("METRO_TEST_MODE", "1");
        builder.ConfigureLogging(logging => logging.AddProvider(TestLogs));
    }
}
