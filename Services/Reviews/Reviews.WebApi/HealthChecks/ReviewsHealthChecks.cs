using Microsoft.Extensions.Diagnostics.HealthChecks;
using MongoDB.Driver;

namespace Reviews.WebApi.HealthChecks;

public static class ReviewsHealthChecks
{
    /// <summary>
    /// Readiness сервісу відгуків:
    /// <list type="bullet">
    /// <item><c>reviews-db</c> — MongoDB ping через зареєстрований IMongoClient, 5 с; без бази сервіс не працює → Unhealthy;</item>
    /// <item><c>redis-cache</c> — Redis PING, 3 с; кеш необов'язковий (читаємо з MongoDB) → Degraded;</item>
    /// <item><c>cache</c> — статистика L1/L2 (hit ratio), лише в /health.</item>
    /// </list>
    /// </summary>
    public static IServiceCollection AddReviewsHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddMongoDb(
                sp => sp.GetRequiredService<IMongoClient>(),
                name: "reviews-db",
                failureStatus: HealthStatus.Unhealthy,
                tags: [HealthTags.Database, "mongodb", HealthTags.Ready],
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
