using FluentValidation;
using Metro.Application.Abstractions.Catalog;
using Metro.Application.Abstractions.Fare;
using Metro.Application.Catalog;
using Metro.Application.Fare;
using Microsoft.Extensions.DependencyInjection;

namespace Metro.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Validation pipeline (DoD BE-01): mọi validator khai báo trong Metro.Application
        // được quét và đăng ký tự động; pipeline auto-validation ở Metro.API gọi chúng
        // trước khi request đi vào controller, trả 400 ValidationProblemDetails khi fail.
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);
        services.AddSingleton<IRouteFinder, RouteFinder>();
        services.AddSingleton<IFareCalculator, FareCalculator>();

        return services;
    }
}
