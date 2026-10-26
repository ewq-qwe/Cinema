using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Reviews.WebApi.Controllers;

/// <summary>
/// Базовий контролер: доступ до ISender (MediatR). Контролери нічого не знають про MongoDB —
/// вони лише перетворюють HTTP-запит на команду/запит і передають його в MediatR.
/// </summary>
[ApiController]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
    private ISender? _mediator;

    protected ISender Mediator => _mediator ??= HttpContext.RequestServices.GetRequiredService<ISender>();

    /// <summary>Версія документа → заголовок ETag (для наступного If-Match).</summary>
    protected void SetETag(int version) => Response.Headers.ETag = $"\"{version}\"";

    /// <summary>If-Match: "3" або W/"3" → 3. Некоректне значення → null.</summary>
    protected static int? ParseIfMatch(string? ifMatch)
    {
        if (string.IsNullOrWhiteSpace(ifMatch)) return null;
        var value = ifMatch.Trim();
        if (value.StartsWith("W/", StringComparison.Ordinal)) value = value[2..];
        return int.TryParse(value.Trim('"'), out var version) && version >= 0 ? version : null;
    }

    protected ObjectResult PreconditionRequired() => Problem(
        statusCode: StatusCodes.Status428PreconditionRequired,
        title: "Потрібен заголовок If-Match",
        detail: "Передайте версію документа з ETag відповіді GET у заголовку If-Match, напр. If-Match: \"0\".");
}
