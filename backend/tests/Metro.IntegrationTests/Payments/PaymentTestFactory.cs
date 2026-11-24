using System.Security.Cryptography;
using System.Text;
using Metro.Application.Abstractions.Payment;
using Metro.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Metro.IntegrationTests.Payments;

/// <summary>Payment-specific factory: fixed JWT key + stub provider gateway.</summary>
public sealed class PaymentTestFactory : WebApplicationFactory<Program>
{
    public TestLogSink TestLogs { get; } = new();

    public const string SigningKey = "be08-integration-signing-key-0123456789-abcdefghijklmnop";
    private readonly string _qrKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var connection = PostgreSqlTestConfiguration.LoadRequired();
        builder.UseEnvironment("Testing");
        builder.UseSetting("DATABASE_URL", connection.ConnectionString);
        builder.UseSetting("Jwt:SigningKey", SigningKey);
        builder.UseSetting("Jwt:Issuer", "metro-api");
        builder.UseSetting("Jwt:Audience", "metro-web");
        builder.UseSetting("Payment:WebhookSecret", StubPaymentGateway.Secret);
        builder.UseSetting("TicketQr:WorkerEnabled", "false");
        builder.UseSetting("TicketQr:ActiveKeyId", "payment-test-v1");
        builder.UseSetting("TicketQr:Keys:payment-test-v1", _qrKey);
        builder.ConfigureLogging(logging => logging.AddProvider(TestLogs));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPaymentGateway>();
            services.AddScoped<IPaymentGateway, StubPaymentGateway>();
        });
    }
}
