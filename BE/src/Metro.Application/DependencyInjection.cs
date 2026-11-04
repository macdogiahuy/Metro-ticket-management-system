using Microsoft.Extensions.DependencyInjection;

namespace Metro.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Register application services, validators, and use cases here.
        return services;
    }
}
