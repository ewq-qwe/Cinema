using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Platform.EventBus.Diagnostics;

/// <summary>
/// Метрики шини подій (Meter "Platform.Messaging", підключено в ServiceDefaults). Теги — короткі фіксовані набори.
/// <list type="bullet">
/// <item><c>messaging.published</c> — опубліковані події (event.type, result: success | failure);</item>
/// <item><c>messaging.consumed</c> — рішення щодо отриманих повідомлень (queue, event.type, result: ack | duplicate | retry | dead_letter);</item>
/// <item><c>messaging.process.duration</c> — тривалість обробки, мс;</item>
/// <item><c>messaging.dlq.depth</c> — глибина DLQ (gauge; асинхронні збої «тихі», тож DLQ треба моніторити).</item>
/// </list>
/// </summary>
public sealed class MessagingMetrics
{
    public const string MeterName = "Platform.Messaging";

    private readonly Counter<long> _published;
    private readonly Counter<long> _consumed;
    private readonly Histogram<double> _duration;
    private readonly ConcurrentDictionary<string, long> _dlqDepth = new();

    public MessagingMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);
        _published = meter.CreateCounter<long>("messaging.published", "{message}", "Опубліковані integration events");
        _consumed = meter.CreateCounter<long>("messaging.consumed", "{message}", "Оброблені повідомлення за рішенням (ack / duplicate / retry / dead_letter)");
        _duration = meter.CreateHistogram<double>("messaging.process.duration", "ms", "Тривалість обробки повідомлення");
        meter.CreateObservableGauge("messaging.dlq.depth", ObserveDlq, "{message}", "Кількість повідомлень у DLQ");
    }

    public void Published(string eventType, bool success) =>
        _published.Add(1, new TagList { { "event.type", eventType }, { "result", success ? "success" : "failure" } });

    public void Consumed(string queue, string eventType, string result, double elapsedMs)
    {
        var tags = new TagList { { "messaging.destination.name", queue }, { "event.type", eventType }, { "result", result } };
        _consumed.Add(1, tags);
        _duration.Record(elapsedMs, tags);
    }

    public void SetDlqDepth(string dlq, long depth) => _dlqDepth[dlq] = depth;

    private IEnumerable<Measurement<long>> ObserveDlq() =>
        _dlqDepth.Select(kv => new Measurement<long>(kv.Value, new KeyValuePair<string, object?>("messaging.destination.name", kv.Key)));
}
