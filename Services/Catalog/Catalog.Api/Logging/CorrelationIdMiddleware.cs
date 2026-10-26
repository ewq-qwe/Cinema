using Serilog.Context;

namespace Catalog.Api.Logging;

/// <summary>
/// Додає до всіх логів запиту структуровані властивості CorrelationId, RequestPath, UserId.
/// CorrelationId береться із заголовка X-Correlation-Id (або генерується) і повертається у відповіді —
/// так інцидент можна знайти в логах кількох сервісів.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext http)
    {
        var correlationId = http.Request.Headers.TryGetValue(HeaderName, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.ToString()
            : http.TraceIdentifier;

        http.Response.Headers[HeaderName] = correlationId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        using (LogContext.PushProperty("RequestPath", http.Request.Path.Value))
        using (LogContext.PushProperty("UserId", http.User.Identity?.IsAuthenticated == true ? http.User.Identity.Name : "anonymous"))
        {
            await next(http);
        }
    }
}
