using Microsoft.AspNetCore.Http;

namespace Platform.ServiceDefaults.Correlation;

/// <summary>
/// DelegatingHandler для всіх HttpClient (підключено в ConfigureHttpClientDefaults):
/// копіює CorrelationId поточного вхідного запиту у вихідний запит до іншого сервісу.
/// </summary>
public sealed class CorrelationIdHandler(IHttpContextAccessor accessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var id = accessor.HttpContext?.Request.Headers[CorrelationIdMiddleware.HeaderName].ToString();
        if (!string.IsNullOrEmpty(id) && !request.Headers.Contains(CorrelationIdMiddleware.HeaderName))
            request.Headers.TryAddWithoutValidation(CorrelationIdMiddleware.HeaderName, id);

        return base.SendAsync(request, ct);
    }
}
