using System.Diagnostics.Metrics;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace Platform.Aggregator.Caching;

/// <summary>
/// Кеш складених відповідей у Redis (IDistributedCache, Aspire). Ключ — з параметрів запиту
/// (aggregator:booking:{id}:full), перевіряється ДО паралельних gRPC-викликів, TTL 30 с — коротший за кеші сервісів,
/// щоб агрегат не виглядав «свіжішим» за дані, з яких зібраний. Часткові відповіді (є warnings) не кешуються.
/// Недоступний Redis не ламає запит — агрегат просто збирається заново.
/// </summary>
public sealed class AggregatedResponseCache(IDistributedCache redis, IMeterFactory meterFactory, ILogger<AggregatedResponseCache> logger)
{
    public const string MeterName = "Platform.Aggregator.Cache";
    public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly TimeSpan Backoff = TimeSpan.FromSeconds(30);
    private long _disabledUntilTicks;   // singleton: після відмови Redis не чекаємо на нього в кожному запиті

    private readonly Counter<long> _hits = meterFactory.Create(MeterName).CreateCounter<long>("cache.hits");
    private readonly Counter<long> _misses = meterFactory.Create(MeterName).CreateCounter<long>("cache.misses");

    public static string BookingKey(long id) => $"aggregator:booking:{id}:full";
    public static string MovieKey(int id) => $"aggregator:movie:{id}:full";

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct) where T : class
    {
        if (IsDisabled) return null;
        try
        {
            var bytes = await redis.GetAsync(key, ct);
            if (bytes is null)
            {
                _misses.Add(1, new KeyValuePair<string, object?>("cache.level", "L2"));
                logger.LogDebug("Aggregated cache miss {CacheKey}", key);
                return null;
            }
            _hits.Add(1, new KeyValuePair<string, object?>("cache.level", "L2"));
            logger.LogInformation("Aggregated cache hit {CacheKey}: gRPC-виклики пропущено", key);
            return JsonSerializer.Deserialize<T>(bytes, Json);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Disable();
            logger.LogWarning(ex, "Redis unavailable — aggregated cache skipped for {CacheKey} (наступні {BackoffSeconds} с без кешу)",
                key, Backoff.TotalSeconds);
            return null;
        }
    }

    public async Task SetAsync<T>(string key, T value, IReadOnlyCollection<string> warnings, CancellationToken ct)
    {
        if (warnings.Count > 0)
        {
            logger.LogInformation("Aggregated response {CacheKey} is partial ({WarningsCount} warnings) — not cached", key, warnings.Count);
            return;
        }
        if (IsDisabled) return;
        try
        {
            await redis.SetAsync(key, JsonSerializer.SerializeToUtf8Bytes(value, Json),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = Ttl }, ct);
            logger.LogDebug("Aggregated response cached {CacheKey} for {TtlSeconds} s", key, Ttl.TotalSeconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Disable();
            logger.LogWarning(ex, "Redis unavailable — aggregated response {CacheKey} not cached", key);
        }
    }

    private bool IsDisabled => DateTime.UtcNow.Ticks < Interlocked.Read(ref _disabledUntilTicks);

    private void Disable() => Interlocked.Exchange(ref _disabledUntilTicks, DateTime.UtcNow.Add(Backoff).Ticks);
}
