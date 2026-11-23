using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Caching.Memory;

namespace Catalog.Api.Metrics;

/// <summary>
/// Бізнес-метрики каталогу (Meter "Catalog.Api", Singleton; Meter додано в ServiceDefaults).
/// <list type="bullet">
/// <item><c>movies.created</c> {movie} — нові фільми, тег age.rating (5 значень);</item>
/// <item><c>movies.updated</c> {movie} — зміни фільмів, тег change (details | description | credits | archive);</item>
/// <item><c>movies.card.requests</c> {request} — запити картки фільму, тег protocol (rest | grpc);</item>
/// <item><c>catalog.operation.duration</c> ms — тривалість операцій (operation, status = success | failure) для ВСІХ виконань;</item>
/// <item><c>catalog.cache.l1.entries</c> {entry} і <c>catalog.cache.l1.size</c> KiBy — gauges поточного стану L1-кешу.</item>
/// </list>
/// Жодних Id у тегах — лише значення з малих фіксованих наборів.
/// </summary>
public sealed class CatalogMetrics
{
    public const string MeterName = "Catalog.Api";

    private readonly Counter<long> _created;
    private readonly Counter<long> _updated;
    private readonly Counter<long> _cardRequests;
    private readonly Histogram<double> _duration;

    public CatalogMetrics(IMeterFactory meterFactory, IMemoryCache memory)
    {
        var meter = meterFactory.Create(MeterName);
        _created = meter.CreateCounter<long>("movies.created", unit: "{movie}", description: "Нові фільми в каталозі");
        _updated = meter.CreateCounter<long>("movies.updated", unit: "{movie}", description: "Зміни фільмів");
        _cardRequests = meter.CreateCounter<long>("movies.card.requests", unit: "{request}", description: "Запити картки фільму");
        _duration = meter.CreateHistogram<double>("catalog.operation.duration", unit: "ms", description: "Тривалість операцій каталогу");

        // Gauge: знімок у момент збору метрик (MemoryCacheOptions.TrackStatistics = true у Program.cs)
        meter.CreateObservableGauge("catalog.cache.l1.entries",
            () => memory.GetCurrentStatistics()?.CurrentEntryCount ?? 0, unit: "{entry}", description: "Записи в L1-кеші");
        meter.CreateObservableGauge("catalog.cache.l1.size",
            () => memory.GetCurrentStatistics()?.CurrentEstimatedSize ?? 0, unit: "KiBy", description: "Розмір L1-кешу (одиниці Size = КБ)");
    }

    public void MovieCreated(string ageRating) => _created.Add(1, new KeyValuePair<string, object?>("age.rating", ageRating));
    public void MovieUpdated(string change) => _updated.Add(1, new KeyValuePair<string, object?>("change", change));
    public void CardRequested(string protocol) => _cardRequests.Add(1, new KeyValuePair<string, object?>("protocol", protocol));

    /// <summary>Вимірює операцію: тривалість записується і для успіху, і для винятку (тег status).</summary>
    public async Task<T> MeasureAsync<T>(string operation, Func<Task<T>> action)
    {
        var sw = Stopwatch.StartNew();
        var success = false;
        try
        {
            var result = await action();
            success = true;
            return result;
        }
        finally
        {
            _duration.Record(sw.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("operation", operation),
                new KeyValuePair<string, object?>("status", success ? "success" : "failure"));
        }
    }

    public Task MeasureAsync(string operation, Func<Task> action) =>
        MeasureAsync(operation, async () => { await action(); return true; });
}
