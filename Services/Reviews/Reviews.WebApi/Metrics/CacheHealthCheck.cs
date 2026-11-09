using Microsoft.Extensions.Diagnostics.HealthChecks;
using Reviews.Application.Common.Caching;

namespace Reviews.WebApi.Metrics;

/// <summary>
/// Статистика кешу в /health: попадання, промахи й hit ratio по рівнях L1 / L2, інвалідації й помилки Redis.
/// Помилки Redis дають Degraded, а не Unhealthy: сервіс працює далі з L1 і MongoDB.
/// </summary>
public sealed class CacheHealthCheck(CacheMetrics metrics) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        var data = metrics.Snapshot();
        return Task.FromResult((long)data["errors"] > 0
            ? HealthCheckResult.Degraded("Були помилки Redis (L2) — кеш працював у режимі L1 + MongoDB.", data: data)
            : HealthCheckResult.Healthy("Кеш L1 + L2 працює.", data));
    }
}
