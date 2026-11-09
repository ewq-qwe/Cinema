using System.Diagnostics;
using Serilog.Core;
using Serilog.Events;

namespace Platform.ServiceDefaults.Logging;

/// <summary>
/// Додає до кожного запису властивості TraceId і SpanId поточного span OpenTelemetry —
/// за ними лог зв'язується з trace у Dashboard навіть у консольному JSON.
/// </summary>
public sealed class TraceContextEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory factory)
    {
        if (Activity.Current is not { } activity) return;
        logEvent.AddPropertyIfAbsent(factory.CreateProperty("TraceId", activity.TraceId.ToHexString()));
        logEvent.AddPropertyIfAbsent(factory.CreateProperty("SpanId", activity.SpanId.ToHexString()));
    }
}
