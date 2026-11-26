using Metro.Application.Abstractions.Auth;
using Metro.Application.Abstractions.Catalog;
using Metro.Application.Abstractions.Fare;
using Metro.Application.Abstractions.MultiTenancy;
using Metro.Application.Abstractions.Network;
using Metro.Application.Abstractions.Storage;
using Metro.Infrastructure.Caching;
using Metro.Infrastructure.Booking;
using Metro.Infrastructure.Catalog;
using Metro.Infrastructure.Configuration;
using Metro.Infrastructure.Fare;
using Metro.Infrastructure.Identity;
using Metro.Infrastructure.Network;
using Metro.Infrastructure.Persistence.Context;
using Metro.Infrastructure.Persistence.MultiTenancy;
using Metro.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Metro.Application.Abstractions.Payment;
using Metro.Application.Abstractions.Reports;
using Metro.Infrastructure.Payments;
using Metro.Infrastructure.Reports;
using StackExchange.Redis;
using Microsoft.AspNetCore.DataProtection;

namespace Metro.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = NeonConnectionString.Resolve(
            configuration["DATABASE_URL"],
            configuration.GetConnectionString("DefaultConnection"));

        services.AddScoped<IOperatorContext, OperatorContext>();

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure(3)));

        services.AddIdentityCore<Microsoft.AspNetCore.Identity.IdentityUser>()
            .AddRoles<Microsoft.AspNetCore.Identity.IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>();

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IAdministrationService, AdministrationService>();
        services.AddScoped<AccountAccess>();
        services.AddScoped<Metro.Application.Abstractions.Gates.IGateService, Metro.Infrastructure.Gates.GateService>();
        services.AddScoped<Metro.Application.Abstractions.Gates.IGateQrSupportService, Metro.Infrastructure.Gates.GateQrSupportService>();
        var dataProtection=services.AddDataProtection().SetApplicationName("Metro.GateEmail");
        if(configuration["GateEmail:KeyRingPath"] is { Length: >0 } keyRingPath)
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));
        services.Configure<Metro.Infrastructure.Gates.GateEmailOptions>(configuration.GetSection("GateEmail"));
        services.AddSingleton<Metro.Infrastructure.Gates.GateEmailProtection>();
        services.AddScoped<Metro.Application.Abstractions.Gates.IGateEmailSender, Metro.Infrastructure.Gates.SmtpGateEmailSender>();
        services.AddScoped<Metro.Infrastructure.Gates.GateEmailProcessor>();
        services.AddHostedService<Metro.Infrastructure.Gates.GateEmailWorker>();
        services.Configure<Metro.Infrastructure.Ticketing.TicketQrOptions>(configuration.GetSection("TicketQr"));
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddScoped<BookingPricing>();
        services.AddScoped<BookingPaymentGuard>();
        services.AddScoped<BookingService>();
        services.AddScoped<Metro.Application.Abstractions.Operations.IOperationsService, Metro.Infrastructure.Operations.OperationsService>();
        services.AddScoped<BookingSuggestionProcessor>();
        services.AddHostedService<BookingSuggestionWorker>();
        services.AddSingleton<Metro.Infrastructure.Ticketing.TicketQrCodec>();
        services.AddScoped<Metro.Application.Abstractions.Ticketing.ITicketService, Metro.Infrastructure.Ticketing.TicketService>();
        services.AddScoped<Metro.Infrastructure.Ticketing.TicketIssuanceProcessor>();
        services.AddHostedService<Metro.Infrastructure.Ticketing.TicketIssuanceWorker>();

        services.Configure<CatalogCacheOptions>(configuration.GetSection("CatalogCache"));
        var redisConnection = new[]
        {
            configuration["Redis:ConnectionString"],
            configuration["Redis__ConnectionString"],
            configuration.GetConnectionString("Redis"),
        }.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        if (!string.IsNullOrWhiteSpace(redisConnection))
        {
            var redisOptions = ConfigurationOptions.Parse(redisConnection);
            redisOptions.AbortOnConnectFail = false;
            if (redisOptions.ConnectTimeout == 5000) redisOptions.ConnectTimeout = 1500;
            if (redisOptions.SyncTimeout == 5000) redisOptions.SyncTimeout = 1500;
            if (redisOptions.AsyncTimeout == 5000) redisOptions.AsyncTimeout = 1500;

            services.AddStackExchangeRedisCache(options =>
            {
                options.ConfigurationOptions = redisOptions;
                options.InstanceName = "metro:";
            });
        }
        else
        {
            services.AddDistributedMemoryCache();
        }

        services.AddSingleton<ICatalogCache, DistributedCatalogCache>();
        services.AddScoped<ICatalogService, CatalogService>();
        services.AddScoped<IFareQuoteService, FareQuoteService>();
        services.AddScoped<IAdminProductService, AdminProductService>();
        services.AddScoped<IAdminFareService, AdminFareService>();
        services.AddScoped<IAdminEligibilityPolicyService, AdminEligibilityPolicyService>();
        services.AddScoped<IAdminNetworkService, AdminNetworkService>();
        services.AddScoped<IAdminScheduleService, AdminScheduleService>();
        services.AddScoped<IAdminTripService, AdminTripService>();
        services.AddScoped<IAdminTripLoadService, AdminTripLoadService>();

        var storageOptions = new NeonStorageOptions
        {
            Bucket = configuration["ObjectStorage:Bucket"] ?? "assets",
            ServiceUrl = configuration["AWS_ENDPOINT_URL_S3"],
            Region = configuration["AWS_REGION"],
            AccessKeyId = configuration["AWS_ACCESS_KEY_ID"],
            SecretAccessKey = configuration["AWS_SECRET_ACCESS_KEY"],
        };

        services.AddSingleton<IObjectStorage>(_ =>
            new NeonObjectStorage(Options.Create(storageOptions)));

        services.AddProfileEligibilityLifecycle();

        // BE-08: MoMo TEST gateway + payment orchestration.
        services.Configure<MoMoOptions>(configuration.GetSection("Payment"));
        services.AddScoped<IPaymentService, PaymentService>();

        // BE-15: reports use administrator membership, never a client-selected operator.
        services.AddScoped<IReportOperatorResolver, ReportOperatorResolver>();
        services.AddScoped<IReportService, ReportService>();

        return services;
    }
}
