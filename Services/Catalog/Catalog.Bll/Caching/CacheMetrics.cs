using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Catalog.Bll.Caching;

/// <summary>
/// Метрики кешу (Meter "Catalog.Cache", підключено в ServiceDefaults → вкладка Metrics у Dashboard):
/// cache.hits / cache.misses з тегом cache.level (L1 | L2) — з них рахується hit ratio кожного рівня;
/// інвалідації з причиною, помилки Redis і тривалість операцій. Лічильники продубльовано полями для health check.
/// </summary>
public sealed class CacheMetrics
{
    public const string MeterName = "Catalog.Cache";

    private readonly Counter<long> _hits;
    private readonly Counter<long> _misses;
    private readonly Counter<long> _invalidations;
    private readonly Counter<long> _errors;
    private readonly Histogram<double> _duration;

    private long _l1Hits, _l1Misses, _l2Hits, _l2Misses, _invalidationCount, _errorCount;

    public CacheMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);
        _hits = meter.CreateCounter<long>("cache.hits", description: "Попадання в кеш");
        _misses = meter.CreateCounter<long>("cache.misses", description: "Промахи кешу");
        _invalidations = meter.CreateCounter<long>("cache.invalidations", description: "Інвалідації після запису");
        _errors = meter.CreateCounter<long>("cache.errors", description: "Помилки рівня кешу (Redis недоступний)");
        _duration = meter.CreateHistogram<double>("cache.operation.duration", unit: "ms", description: "Тривалість операцій з кешем");
    }

    public void Hit(string level)
    {
        _hits.Add(1, new KeyValuePair<string, object?>("cache.level", level));
        if (level == CacheLevels.L1) Interlocked.Increment(ref _l1Hits); else Interlocked.Increment(ref _l2Hits);
    }

    public void Miss(string level)
    {
        _misses.Add(1, new KeyValuePair<string, object?>("cache.level", level));
        if (level == CacheLevels.L1) Interlocked.Increment(ref _l1Misses); else Interlocked.Increment(ref _l2Misses);
    }

    public void Invalidated(string reason)
    {
        _invalidations.Add(1, new KeyValuePair<string, object?>("cache.reason", reason));
        Interlocked.Increment(ref _invalidationCount);
    }

    public void Error(string level)
    {
        _errors.Add(1, new KeyValuePair<string, object?>("cache.level", level));
        Interlocked.Increment(ref _errorCount);
    }

    public void Duration(string level, string operation, double ms) =>
        _duration.Record(ms, new TagList { { "cache.level", level }, { "cache.operation", operation } });

    /// <summary>Знімок статистики для health check: попадання/промахи й hit ratio по рівнях.</summary>
    public IReadOnlyDictionary<string, object> Snapshot() => new Dictionary<string, object>
    {
        ["l1.hits"] = Interlocked.Read(ref _l1Hits),
        ["l1.misses"] = Interlocked.Read(ref _l1Misses),
        ["l1.hitRatio"] = Ratio(_l1Hits, _l1Misses),
        ["l2.hits"] = Interlocked.Read(ref _l2Hits),
        ["l2.misses"] = Interlocked.Read(ref _l2Misses),
        ["l2.hitRatio"] = Ratio(_l2Hits, _l2Misses),
        ["invalidations"] = Interlocked.Read(ref _invalidationCount),
        ["errors"] = Interlocked.Read(ref _errorCount)
    };

    private static double Ratio(long hits, long misses) =>
        hits + misses == 0 ? 0 : Math.Round(hits / (double)(hits + misses), 3);
}

public static class CacheLevels
{
    public const string L1 = "L1";   // IMemoryCache
    public const string L2 = "L2";   // Redis (IDistributedCache)
}
