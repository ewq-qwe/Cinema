using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Catalog.Bll.Caching;

/// <summary>Дворівневий кеш: L1 — пам'ять процесу, L2 — Redis (спільний для всіх реплік), далі — база даних.</summary>
public interface ITwoLevelCache
{
    /// <summary>Cache-aside: L1 → L2 → factory (БД); рівні заповнюються на зворотному шляху.</summary>
    Task<T> GetOrCreateAsync<T>(string key, CachePolicy policy, Func<CancellationToken, Task<T>> factory, CancellationToken ct)
        where T : class;

    /// <summary>Інвалідація після запису — на обох рівнях, синхронно з операцією.</summary>
    Task RemoveAsync(string key, string reason, CancellationToken ct);

    /// <summary>Поточна версія групи ключів (напр., усіх сторінок списку).</summary>
    Task<string> GetVersionAsync(string versionKey, CancellationToken ct);

    /// <summary>Масова інвалідація: нова версія робить усі старі ключі групи недосяжними.</summary>
    Task BumpVersionAsync(string versionKey, string reason, CancellationToken ct);
}

/// <summary>
/// Ручна реалізація дворівневого кешу (порівн. HybridCache у .NET):
/// <list type="bullet">
/// <item>L1 — IMemoryCache з Size (у КБ серіалізованого значення), sliding і absolute expiration;</item>
/// <item>L2 — IDistributedCache поверх Redis (Aspire), значення — JSON (System.Text.Json), TTL за типом даних;</item>
/// <item>недоступний Redis не валить сервіс: L2 пропускається на 30 с, дані йдуть з L1 / БД (повільніше).</item>
/// </list>
/// </summary>
public sealed class TwoLevelCache(
    IMemoryCache memory,
    IDistributedCache redis,
    CacheMetrics metrics,
    ILogger<TwoLevelCache> logger) : ITwoLevelCache
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan L2Backoff = TimeSpan.FromSeconds(30);

    private long _l2DisabledUntilTicks;   // кеш — singleton: після відмови Redis не смикаємо його на кожен запит

    public async Task<T> GetOrCreateAsync<T>(string key, CachePolicy policy, Func<CancellationToken, Task<T>> factory, CancellationToken ct)
        where T : class
    {
        // ---------- L1: пам'ять процесу ----------
        if (memory.TryGetValue(key, out T? cached) && cached is not null)
        {
            metrics.Hit(CacheLevels.L1);
            logger.LogDebug("Cache hit {CacheKey} at {CacheLevel}", key, CacheLevels.L1);
            return cached;
        }
        metrics.Miss(CacheLevels.L1);

        // ---------- L2: Redis ----------
        var bytes = await TryRedisAsync("get", () => redis.GetAsync(key, ct));
        if (bytes is not null)
        {
            var fromRedis = JsonSerializer.Deserialize<T>(bytes, Json)!;
            metrics.Hit(CacheLevels.L2);
            SetL1(key, fromRedis, bytes.Length, policy);   // заповнюємо L1 на зворотному шляху
            logger.LogDebug("Cache hit {CacheKey} at {CacheLevel}, {SizeBytes} bytes", key, CacheLevels.L2, bytes.Length);
            return fromRedis;
        }
        metrics.Miss(CacheLevels.L2);
        logger.LogDebug("Cache miss {CacheKey} — reading from database", key);

        // ---------- база даних ----------
        var value = await factory(ct);
        var serialized = JsonSerializer.SerializeToUtf8Bytes(value, Json);
        await TryRedisAsync<object?>("set", async () =>
        {
            await redis.SetAsync(key, serialized,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = policy.L2Absolute }, ct);
            return null;
        });
        SetL1(key, value, serialized.Length, policy);
        logger.LogInformation("Cache filled {CacheKey}: {SizeBytes} bytes, L1 TTL {L1Ttl}, L2 TTL {L2Ttl}",
            key, serialized.Length, policy.L1Sliding ?? policy.L1Absolute, policy.L2Absolute);
        return value;
    }

    public async Task RemoveAsync(string key, string reason, CancellationToken ct)
    {
        memory.Remove(key);
        await TryRedisAsync<object?>("remove", async () =>
        {
            await redis.RemoveAsync(key, ct);
            return null;
        });
        metrics.Invalidated(reason);
        logger.LogInformation("Cache invalidated {CacheKey}, reason {Reason}", key, reason);
    }

    public async Task<string> GetVersionAsync(string versionKey, CancellationToken ct) =>
        (await GetOrCreateAsync(versionKey, CachePolicy.Version, _ => Task.FromResult(new CacheVersion(NewVersion())), ct)).Value;

    public async Task BumpVersionAsync(string versionKey, string reason, CancellationToken ct)
    {
        var version = new CacheVersion(NewVersion());
        var bytes = JsonSerializer.SerializeToUtf8Bytes(version, Json);
        await TryRedisAsync<object?>("set", async () =>
        {
            await redis.SetAsync(versionKey, bytes,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CachePolicy.Version.L2Absolute }, ct);
            return null;
        });
        SetL1(versionKey, version, bytes.Length, CachePolicy.Version);
        metrics.Invalidated(reason);
        logger.LogInformation("Cache invalidated {CacheKey} (new version {Version}), reason {Reason}", versionKey, version.Value, reason);
    }

    private void SetL1<T>(string key, T value, int sizeBytes, CachePolicy policy) =>
        memory.Set(key, value, new MemoryCacheEntryOptions
        {
            Size = Math.Max(1, sizeBytes / 1024),                 // «одиниці» SizeLimit = КБ
            SlidingExpiration = policy.L1Sliding,
            AbsoluteExpirationRelativeToNow = policy.L1Absolute,
            Priority = policy.L1Sliding is null ? CacheItemPriority.High : CacheItemPriority.Normal
        });

    /// <summary>Операція з Redis: помилка → попередження, метрика й тимчасове відключення L2 замість винятку.</summary>
    private async Task<TResult?> TryRedisAsync<TResult>(string operation, Func<Task<TResult?>> action)
    {
        if (DateTime.UtcNow.Ticks < Interlocked.Read(ref _l2DisabledUntilTicks)) return default;

        var sw = Stopwatch.StartNew();
        try
        {
            var result = await action();
            metrics.Duration(CacheLevels.L2, operation, sw.Elapsed.TotalMilliseconds);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            metrics.Error(CacheLevels.L2);
            Interlocked.Exchange(ref _l2DisabledUntilTicks, DateTime.UtcNow.Add(L2Backoff).Ticks);
            logger.LogWarning(ex, "Redis (L2) unavailable on {CacheOperation}: працюємо з L1 і БД наступні {BackoffSeconds} с",
                operation, L2Backoff.TotalSeconds);
            return default;
        }
    }

    private static string NewVersion() => Guid.NewGuid().ToString("N")[..8];

    public sealed record CacheVersion(string Value);
}
