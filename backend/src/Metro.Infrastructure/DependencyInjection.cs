using Metro.Application.Abstractions.Auth;
using Metro.Application.Abstractions.Catalog;
using Metro.Application.Abstractions.Fare;
using Metro.Application.Abstractions.MultiTenancy;
using Metro.Application.Abstractions.Storage;
using Metro.Infrastructure.Caching;
using Metro.Infrastructure.Catalog;
using Metro.Infrastructure.Configuration;
using Metro.Infrastructure.Fare;
using Metro.Infrastructure.Identity;
using Metro.Infrastructure.Persistence.Context;
using Metro.Infrastructure.Persistence.MultiTenancy;
using Metro.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Metro.Application.Abstractions.Payment;
using Metro.Infrastructure.Payments;
using StackExchange.Redis;

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

        // BE-08: MoMo TEST gateway + payment orchestration.
        services.Configure<MoMoOptions>(configuration.GetSection("Payment"));
        services.AddScoped<IPaymentService, PaymentService>();

        return services;
    }
}
