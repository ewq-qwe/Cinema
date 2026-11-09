using MediatR;
using Reviews.Application.Common.Caching;

namespace Reviews.Application.Common.Behaviors;

/// <summary>
/// Cache-aside для запитів, позначених <see cref="ICacheableQuery"/>: кешується результат обробника.
/// Стоїть після ValidationBehavior (некоректні запити не кешуються) і перед PerformanceBehavior
/// (той міряє лише реальні звернення до обробника/MongoDB, тобто промахи).
/// Команди проходять наскрізь; інвалідацію виконують їхні обробники.
/// </summary>
public sealed class CachingBehavior<TRequest, TResponse>(ITwoLevelCache cache)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        if (request is not ICacheableQuery { CacheKey: { } baseKey } cacheable)
            return await next(ct);

        var key = cacheable.IsListQuery
            ? $"{baseKey}:v{await cache.GetVersionAsync(ReviewsCacheKeys.ListVersion, ct)}"
            : baseKey;

        return await cache.GetOrCreateAsync(key, cacheable.CachePolicy, token => next(token), ct);
    }
}
