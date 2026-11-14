using Metro.Application.Abstractions.Storage;
using Metro.Infrastructure.Configuration;
using Metro.Infrastructure.Persistence.Context;
using Metro.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Metro.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = NeonConnectionString.Resolve(
            configuration["DATABASE_URL"],
            configuration.GetConnectionString("DefaultConnection"));

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql => npgsql.EnableRetryOnFailure(3)));

        services.AddIdentityCore<Microsoft.AspNetCore.Identity.IdentityUser>()
            .AddEntityFrameworkStores<ApplicationDbContext>();

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

        return services;
    }
}
