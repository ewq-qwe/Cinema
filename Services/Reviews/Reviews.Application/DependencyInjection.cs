using System.Globalization;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Reviews.Application.Common.Behaviors;
using Reviews.Application.Common.Caching;

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
            cfg.AddOpenBehavior(typeof(CachingBehavior<,>));     // 3 — кеш лише для валідних запитів (Лаб. 06)
            cfg.AddOpenBehavior(typeof(PerformanceBehavior<,>)); // 4 — біля обробника
        });

        services.AddValidatorsFromAssembly(assembly);

        // ---------- Лаб. 06: дворівневий кеш. L1 — пам'ять з обмеженням розміру; L2 — IDistributedCache.
        // WebApi підключає Redis (AddRedisDistributedCache); без нього (тести) — запасний кеш у пам'яті через TryAdd.
        services.AddMemoryCache(o =>
        {
            o.SizeLimit = config.GetValue("Caching:MemorySizeLimitKb", 20_000);
            o.CompactionPercentage = 0.25;
        });
        services.TryAddSingleton<Microsoft.Extensions.Caching.Distributed.IDistributedCache>(sp =>
            new Microsoft.Extensions.Caching.Distributed.MemoryDistributedCache(
                Microsoft.Extensions.Options.Options.Create(new Microsoft.Extensions.Caching.Memory.MemoryDistributedCacheOptions())));
        services.AddMetrics();
        services.TryAddSingleton<CacheMetrics>();
        services.TryAddSingleton<ITwoLevelCache, TwoLevelCache>();
        ValidatorOptions.Global.LanguageManager.Culture = new CultureInfo("uk"); // стандартні повідомлення — українською

        return services;
    }
}
