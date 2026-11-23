using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Catalog.Api.HealthChecks;

public static class CatalogHealthChecks
{
    /// <summary>
    /// Readiness каталогу:
    /// <list type="bullet">
    /// <item><c>catalog-db</c> — SQL Server, <c>SELECT 1</c>, 5 с; без БД каталог не працює → Unhealthy;</item>
    /// <item><c>redis-cache</c> — Redis PING, 3 с; кеш необов'язковий (читаємо з БД) → Degraded, а не Unhealthy;</item>
    /// <item><c>cache</c> — статистика L1/L2 (hit ratio); Degraded, якщо були помилки Redis (лише в /health).</item>
    /// </list>
    /// </summary>
    public static IServiceCollection AddCatalogHealthChecks(this IServiceCollection services, IConfiguration config)
    {
        services.AddHealthChecks()
            .AddSqlServer(
                connectionString: config.GetConnectionString("CatalogDb")!,
                healthQuery: "SELECT 1;",
                name: "catalog-db",
                failureStatus: HealthStatus.Unhealthy,
                tags: [HealthTags.Database, "sql", HealthTags.Ready],
                timeout: TimeSpan.FromSeconds(5))
            .AddRedis(
                sp => sp.GetRequiredService<IConfiguration>().GetConnectionString("redis")!,
                name: "redis-cache",
                failureStatus: HealthStatus.Degraded,
                tags: [HealthTags.Cache, "redis", HealthTags.Ready],
                timeout: TimeSpan.FromSeconds(3))
            .AddCheck<CacheHealthCheck>("cache", tags: [HealthTags.Cache]);
        return services;
    }
}
