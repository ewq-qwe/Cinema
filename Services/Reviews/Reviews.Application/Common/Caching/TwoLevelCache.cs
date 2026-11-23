using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Reviews.Application.Common.Caching;

/// <summary>
/// Дворівневий кеш сервісу відгуків: L1 — пам'ять процесу, L2 — Redis (спільний для реплік), далі — обробник (MongoDB).
/// Інтерфейс у шарі Application; реалізації кешів (IMemoryCache, IDistributedCache) — абстракції .NET, тож
/// Application і далі не залежить від Infrastructure.
/// </summary>
public interface ITwoLevelCache
{
    Task<T> GetOrCreateAsync<T>(string key, CachePolicy policy, Func<CancellationToken, Task<T>> factory, CancellationToken ct);
    Task RemoveAsync(string key, string reason, CancellationToken ct);
    Task<string> GetVersionAsync(string versionKey, CancellationToken ct);
    Task BumpVersionAsync(string versionKey, string reason, CancellationToken ct);
}

/// <inheritdoc cref="ITwoLevelCache"/>
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
    {
        if (memory.TryGetValue(key, out T? cached) && cached is not null)
        {
            metrics.Hit(CacheLevels.L1);
            logger.LogDebug("Cache hit {CacheKey} at {CacheLevel}", key, CacheLevels.L1);
            return cached;
        }
        metrics.Miss(CacheLevels.L1);

        var bytes = await TryRedisAsync("get", () => redis.GetAsync(key, ct));
        if (bytes is not null)
        {
            var fromRedis = JsonSerializer.Deserialize<T>(bytes, Json)!;
            metrics.Hit(CacheLevels.L2);
            SetL1(key, fromRedis, bytes.Length, policy);
            logger.LogDebug("Cache hit {CacheKey} at {CacheLevel}, {SizeBytes} bytes", key, CacheLevels.L2, bytes.Length);
            return fromRedis;
        }
        metrics.Miss(CacheLevels.L2);
        logger.LogDebug("Cache miss {CacheKey} — reading from MongoDB", key);

        var value = await factory(ct);
        if (value is null) return value;   // «немає даних» не кешуємо — інакше 404 пережив би створення документа

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
            Size = Math.Max(1, sizeBytes / 1024),
            SlidingExpiration = policy.L1Sliding,
            AbsoluteExpirationRelativeToNow = policy.L1Absolute,
            Priority = policy.L1Sliding is null ? CacheItemPriority.High : CacheItemPriority.Normal
        });

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
            logger.LogWarning(ex, "Redis (L2) unavailable on {CacheOperation}: працюємо з L1 і MongoDB наступні {BackoffSeconds} с",
                operation, L2Backoff.TotalSeconds);
            return default;
        }
    }

    private static string NewVersion() => Guid.NewGuid().ToString("N")[..8];

    public sealed record CacheVersion(string Value);
}
