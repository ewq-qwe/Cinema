using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Platform.Aggregator.Metrics;

/// <summary>
/// Бізнес-метрики агрегатора (Meter "Platform.Aggregator", Singleton, підключено в ServiceDefaults):
/// <list type="bullet">
/// <item><c>aggregator.cards</c> — зібрані картки; теги card (booking | movie), result (full | partial | not_found | unavailable), transport;</item>
/// <item><c>aggregator.card.duration</c> — тривалість агрегації в мс для ВСІХ результатів, включно з невдалими;</item>
/// <item><c>aggregator.requests.in_flight</c> — gauge: скільки агрегацій виконується зараз.</item>
/// </list>
/// Теги мають кілька фіксованих значень — без Id бронювань, фільмів чи користувачів (низька кардинальність).
/// </summary>
public sealed class AggregatorMetrics
{
    public const string MeterName = "Platform.Aggregator";

    private readonly Counter<long> _cards;
    private readonly Histogram<double> _duration;
    private long _inFlight;

    public AggregatorMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);
        _cards = meter.CreateCounter<long>("aggregator.cards", unit: "{card}", description: "Зібрані агреговані картки");
        _duration = meter.CreateHistogram<double>("aggregator.card.duration", unit: "ms", description: "Тривалість агрегації");
        meter.CreateObservableGauge("aggregator.requests.in_flight", () => Interlocked.Read(ref _inFlight),
            unit: "{request}", description: "Агрегації, що виконуються зараз");
    }

    /// <summary>Вимірює агрегацію: тривалість записується і для успіху, і для винятку.</summary>
    public async Task<T> MeasureAsync<T>(string card, string transport, Func<Task<T>> aggregate, Func<T, string> result)
    {
        Interlocked.Increment(ref _inFlight);
        var sw = Stopwatch.StartNew();
        var outcome = "unavailable";
        try
        {
            var value = await aggregate();
            outcome = result(value);
            return value;
        }
        finally
        {
            Interlocked.Decrement(ref _inFlight);
            var tags = new TagList { { "card", card }, { "result", outcome }, { "transport", transport } };
            _cards.Add(1, tags);
            _duration.Record(sw.Elapsed.TotalMilliseconds, tags);
        }
    }
}
