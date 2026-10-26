using Catalog.Bll.Dtos;
using FluentValidation.Results;
using Microsoft.AspNetCore.Mvc;

namespace Catalog.Api.Controllers;

internal static class ControllerExtensions
{
    /// <summary>
    /// Результат FluentValidation → 400 ValidationProblemDetails (помилки по полях) через стандартну
    /// ProblemDetailsFactory — той самий формат (type, title, status, errors, traceId), що й для DataAnnotations.
    /// </summary>
    public static ActionResult ValidationProblem(this ControllerBase controller, ValidationResult result)
    {
        foreach (var error in result.Errors)
            controller.ModelState.AddModelError(error.PropertyName, error.ErrorMessage);
        return controller.ValidationProblem(controller.ModelState);
    }

    /// <summary>Метадані пагінації дублюються в заголовках (зручно для кешування й клієнтів без розбору тіла).</summary>
    public static void AddPaginationHeaders<T>(this HttpResponse response, PagedResult<T> page)
    {
        response.Headers["X-Pagination-TotalCount"] = page.TotalCount.ToString();
        response.Headers["X-Pagination-TotalPages"] = page.TotalPages.ToString();
        response.Headers["X-Pagination-Page"] = page.Page.ToString();
        response.Headers["X-Pagination-PageSize"] = page.PageSize.ToString();
    }
}
