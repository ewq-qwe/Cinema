using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Serilog.Context;

namespace Platform.ServiceDefaults.Correlation;

/// <summary>
/// Наскрізний ідентифікатор звернення. Береться із заголовка X-Correlation-Id або створюється (GUID) —
/// зазвичай у Gateway, а в сервісах лише тоді, коли до них звертаються в обхід Gateway.
/// Ідентифікатор записується в Request.Headers (його побачать YARP і CorrelationIdHandler),
/// у LogContext (потрапляє в усі логи запиту), у тег поточного span і в заголовок відповіді.
/// Межі запиту логуються: початок і кінець зі статусом і тривалістю.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-Id";
    public const string LogProperty = "CorrelationId";
    public const string SpanTag = "correlation.id";

    public async Task InvokeAsync(HttpContext context)
    {
        var generated = false;
        if (!context.Request.Headers.TryGetValue(HeaderName, out var value) || string.IsNullOrWhiteSpace(value))
        {
            value = Guid.NewGuid().ToString();
            context.Request.Headers[HeaderName] = value;   // далі його побачать YARP і HttpClient
            generated = true;
        }

        var correlationId = value.ToString();
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });
        Activity.Current?.SetTag(SpanTag, correlationId);

        using (LogContext.PushProperty(LogProperty, correlationId))
        using (LogContext.PushProperty("RequestPath", context.Request.Path.Value))
        {
            if (generated)
                logger.LogInformation("CorrelationId {CorrelationId} згенеровано для вхідного запиту без заголовка {Header:l}",
                    correlationId, HeaderName);

            if (IsInfrastructurePath(context.Request.Path))
            {
                await next(context);
                return;
            }

            var sw = Stopwatch.StartNew();
            logger.LogInformation("Request started {Method:l} {Path:l}{Query:l} from {ClientIp:l}",
                context.Request.Method, context.Request.Path, context.Request.QueryString, context.Connection.RemoteIpAddress?.ToString());
            try
            {
                await next(context);
            }
            finally
            {
                logger.LogInformation("Request finished {Method:l} {Path:l} with {StatusCode} in {ElapsedMs} ms",
                    context.Request.Method, context.Request.Path, context.Response.StatusCode, sw.ElapsedMilliseconds);
            }
        }
    }

    /// <summary>Health-перевірки й статика Swagger не засмічують лог межами запиту.</summary>
    private static bool IsInfrastructurePath(PathString path) =>
        path.StartsWithSegments("/health") || path.StartsWithSegments("/alive") || path.StartsWithSegments("/swagger");
}
