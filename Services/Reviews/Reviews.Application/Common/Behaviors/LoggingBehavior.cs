using System.Diagnostics;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Reviews.Application.Common.Behaviors;

/// <summary>
/// Зовнішній behavior: структуровано логує кожну команду/запит, результат і час.
/// Якщо запит відхилено валідацією, у лозі видно, що до обробника він не дійшов.
/// Винятки тут лише логуються; у HTTP-статуси їх перетворює WebApi.
/// </summary>
public sealed class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var name = typeof(TRequest).Name;
        logger.LogInformation("MediatR → {RequestName} {@Request}", name, request);
        var sw = Stopwatch.StartNew();

        try
        {
            var response = await next(ct);
            logger.LogInformation("MediatR ← {RequestName} оброблено за {ElapsedMs} мс", name, sw.ElapsedMilliseconds);
            return response;
        }
        catch (ValidationException ex)
        {
            logger.LogWarning("MediatR ✗ {RequestName} відхилено валідацією — обробник НЕ викликався. Помилки: {@ValidationErrors}",
                name, ex.Errors.Select(e => new { e.PropertyName, e.ErrorMessage }));
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning("MediatR ✗ {RequestName} завершився винятком {ExceptionType} за {ElapsedMs} мс: {Reason}",
                name, ex.GetType().Name, sw.ElapsedMilliseconds, ex.Message);
            throw;
        }
    }
}
