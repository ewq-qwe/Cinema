using System.Collections.Concurrent;
using System.Diagnostics;
using Platform.Aggregator.Models;

namespace Platform.Aggregator.Services;

public enum CallStatus { Ok, NotFound, Unavailable }

public readonly record struct CallResult<T>(CallStatus Status, T? Value)
{
    public bool IsOk => Status == CallStatus.Ok;
}

/// <summary>Сервіс, без якого відповідь сформувати неможливо (основний ресурс картки), недоступний → 503.</summary>
public sealed class UpstreamUnavailableException(string service, Exception inner)
    : Exception($"Сервіс {service} недоступний — основні дані отримати неможливо.", inner)
{
    public string Service { get; } = service;
}

/// <summary>
/// Обгортка одного агрегувального запиту: логує кожен виклик сервісу та його тривалість, перетворює відмови
/// на попередження (часткова відповідь замість 500) і збирає звіт про виклики. Потокобезпечна —
/// виклики йдуть паралельно (Task.WhenAll).
/// </summary>
public sealed class CallTracker(ILogger logger, CancellationToken ct)
{
    private readonly ConcurrentQueue<SourceCallDto> _sources = new();
    private readonly List<string> _warnings = [];
    private readonly ConcurrentDictionary<string, ConcurrentQueue<string>> _unavailable = new();

    public IReadOnlyList<SourceCallDto> Sources => _sources.ToArray();

    /// <summary>Одне попередження на недоступний сервіс (з переліком пропущених даних) + попередження узгодженості.</summary>
    public IReadOnlyList<string> Warnings
    {
        get
        {
            var unavailable = _unavailable.OrderBy(kv => kv.Key)
                .Select(kv => $"{kv.Key} is unavailable — пропущено: {string.Join(", ", kv.Value)}.");
            lock (_warnings) return unavailable.Concat(_warnings).ToArray();
        }
    }

    public void Warn(string warning)
    {
        lock (_warnings)
            if (!_warnings.Contains(warning)) _warnings.Add(warning);
    }

    /// <summary>Необов'язкові дані: недоступність сервісу → попередження й порожній результат.</summary>
    public async Task<CallResult<T>> TryAsync<T>(string service, string operation, Func<Task<T?>> call)
    {
        var sw = Stopwatch.StartNew();
        logger.LogInformation("Calling {Service:l}: {Operation:l}", service, operation);
        try
        {
            var value = await call();
            var status = value is null ? CallStatus.NotFound : CallStatus.Ok;
            Record(service, operation, status, sw);
            return new CallResult<T>(status, value);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)   // скасування клієнтом — не відмова сервісу
        {
            Record(service, operation, CallStatus.Unavailable, sw);
            logger.LogWarning(ex, "{Service:l} is unavailable: {Operation:l} failed after {ElapsedMs} ms ({ErrorType}: {Error})",
                service, operation, sw.ElapsedMilliseconds, ex.GetType().Name, ex.Message);
            _unavailable.GetOrAdd(service, _ => new()).Enqueue(operation);
            return new CallResult<T>(CallStatus.Unavailable, default);
        }
    }

    /// <summary>Основні дані: недоступність сервісу → UpstreamUnavailableException (503); 404 → null.</summary>
    public async Task<T?> RequiredAsync<T>(string service, string operation, Func<Task<T?>> call)
    {
        var result = await TryAsync(service, operation, call);
        if (result.Status == CallStatus.Unavailable)
            throw new UpstreamUnavailableException(service, new HttpRequestException($"{service}: {operation} failed"));
        return result.Value;
    }

    private void Record(string service, string operation, CallStatus status, Stopwatch sw)
    {
        sw.Stop();
        _sources.Enqueue(new SourceCallDto(service, operation, status.ToString(), sw.ElapsedMilliseconds));
        if (status != CallStatus.Unavailable)
            logger.LogInformation("{Service:l} responded in {ElapsedMs} ms ({CallStatus}): {Operation:l}",
                service, sw.ElapsedMilliseconds, status, operation);
    }
}
