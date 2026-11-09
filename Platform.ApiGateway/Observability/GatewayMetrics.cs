using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Platform.ApiGateway.Observability;

/// <summary>
/// Метрики трафіку Gateway (Meter = ім'я застосунку, підключено в ServiceDefaults → вкладка Metrics у Dashboard):
/// кількість запитів (з неї — запити за секунду й частка помилок), тривалість відповіді, помилки проксування.
/// </summary>
public sealed class GatewayMetrics
{
    public const string MeterName = "Platform.ApiGateway";

    private readonly Counter<long> _requests;
    private readonly Histogram<double> _duration;
    private readonly Counter<long> _proxyErrors;

    public GatewayMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);
        _requests = meter.CreateCounter<long>("gateway.requests", description: "Запити через Gateway");
        _duration = meter.CreateHistogram<double>("gateway.request.duration", unit: "ms", description: "Тривалість відповіді");
        _proxyErrors = meter.CreateCounter<long>("gateway.proxy.errors", description: "Помилки звернення до сервісів");
    }

    public void Record(string route, string cluster, int statusCode, double elapsedMs)
    {
        var tags = new TagList { { "route", route }, { "cluster", cluster }, { "status_code", statusCode } };
        _requests.Add(1, tags);
        _duration.Record(elapsedMs, tags);
    }

    public void RecordProxyError(string route, string cluster, string error) =>
        _proxyErrors.Add(1, new TagList { { "route", route }, { "cluster", cluster }, { "error", error } });
}
