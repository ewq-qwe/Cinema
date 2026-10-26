using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Reviews.Application.Common.Behaviors;

/// <summary>
/// Найближчий до обробника behavior: міряє час самого обробника (разом із запитами до MongoDB)
/// і пише попередження, якщо він працює аномально довго.
/// </summary>
public sealed class PerformanceBehavior<TRequest, TResponse>(ILogger<PerformanceBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    public const int SlowThresholdMs = 500;

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var response = await next(ct);
        sw.Stop();

        if (sw.ElapsedMilliseconds > SlowThresholdMs)
            logger.LogWarning("Повільний обробник {RequestName}: {ElapsedMs} мс (поріг {ThresholdMs} мс) {@Request}",
                typeof(TRequest).Name, sw.ElapsedMilliseconds, SlowThresholdMs, request);
        else
            logger.LogDebug("Обробник {RequestName} виконано за {ElapsedMs} мс", typeof(TRequest).Name, sw.ElapsedMilliseconds);

        return response;
    }
}
