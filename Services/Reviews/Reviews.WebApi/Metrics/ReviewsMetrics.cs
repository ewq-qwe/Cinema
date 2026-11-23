using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Caching.Memory;

namespace Reviews.WebApi.Metrics;

/// <summary>
/// Бізнес-метрики відгуків (Meter "Reviews.WebApi", Singleton; Meter додано в ServiceDefaults).
/// <list type="bullet">
/// <item><c>reviews.created</c> {review} — нові відгуки, теги review.kind (quick | full | critic) і review.rating (1..5);</item>
/// <item><c>reviews.updated</c>, <c>reviews.deleted</c> {review}, <c>reviews.comments.added</c> {comment},
/// <c>discussions.messages.posted</c> {message};</item>
/// <item><c>reviews.operation.duration</c> ms — тривалість операцій (operation, status = success | failure) для ВСІХ виконань;</item>
/// <item><c>reviews.cache.l1.entries</c> {entry} — gauge: записи в L1-кеші.</item>
/// </list>
/// Теги лише з малих фіксованих наборів: жодних Id відгуків, фільмів чи глядачів.
/// </summary>
public sealed class ReviewsMetrics
{
    public const string MeterName = "Reviews.WebApi";

    private readonly Counter<long> _created;
    private readonly Counter<long> _updated;
    private readonly Counter<long> _deleted;
    private readonly Counter<long> _comments;
    private readonly Counter<long> _messages;
    private readonly Histogram<double> _duration;

    public ReviewsMetrics(IMeterFactory meterFactory, IMemoryCache memory)
    {
        var meter = meterFactory.Create(MeterName);
        _created = meter.CreateCounter<long>("reviews.created", unit: "{review}", description: "Нові відгуки");
        _updated = meter.CreateCounter<long>("reviews.updated", unit: "{review}", description: "Відредаговані відгуки");
        _deleted = meter.CreateCounter<long>("reviews.deleted", unit: "{review}", description: "Видалені відгуки");
        _comments = meter.CreateCounter<long>("reviews.comments.added", unit: "{comment}", description: "Коментарі до відгуків");
        _messages = meter.CreateCounter<long>("discussions.messages.posted", unit: "{message}", description: "Повідомлення в обговореннях");
        _duration = meter.CreateHistogram<double>("reviews.operation.duration", unit: "ms", description: "Тривалість операцій відгуків");
        meter.CreateObservableGauge("reviews.cache.l1.entries",
            () => memory.GetCurrentStatistics()?.CurrentEntryCount ?? 0, unit: "{entry}", description: "Записи в L1-кеші");
    }

    public void ReviewCreated(string kind, int rating) => _created.Add(1,
        new KeyValuePair<string, object?>("review.kind", kind),
        new KeyValuePair<string, object?>("review.rating", rating));

    public void ReviewUpdated() => _updated.Add(1);
    public void ReviewDeleted() => _deleted.Add(1);
    public void CommentAdded() => _comments.Add(1);
    public void MessagePosted() => _messages.Add(1);

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
}
