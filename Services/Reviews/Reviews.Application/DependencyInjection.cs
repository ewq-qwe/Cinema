using System.Globalization;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Reviews.Application.Common.Behaviors;

namespace Reviews.Application;

public static class DependencyInjection
{
    /// <summary>
    /// MediatR (обробники зі сканування збірки + pipeline behaviors) і валідатори FluentValidation.
    /// Порядок AddOpenBehavior = порядок виконання: Logging (зовнішній) → Validation → Performance (біля обробника).
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration config)
    {
        var assembly = typeof(DependencyInjection).Assembly;

        services.AddMediatR(cfg =>
        {
            var licenseKey = config["LuckyPenny:LicenseKey"];
            if (!string.IsNullOrWhiteSpace(licenseKey)) cfg.LicenseKey = licenseKey;

            cfg.RegisterServicesFromAssembly(assembly);
            cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));     // 1 — зовнішній
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));  // 2
            cfg.AddOpenBehavior(typeof(PerformanceBehavior<,>)); // 3 — біля обробника
        });

        services.AddValidatorsFromAssembly(assembly);
        ValidatorOptions.Global.LanguageManager.Culture = new CultureInfo("uk"); // стандартні повідомлення — українською

        return services;
    }
}
