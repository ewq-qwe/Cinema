using Catalog.Domain.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Catalog.Api.Errors;

/// <summary>
/// Єдиний глобальний обробник: виняток → HTTP-статус у форматі ProblemDetails.
/// EntityNotFound → 404, Validation → 400 (ValidationProblemDetails), Conflict / Domain → 409, решта → 500.
/// Кожен виняток логується ОДИН раз — тут. Подробиці 500 віддаються лише в Development.
/// </summary>
public sealed class ExceptionToProblemDetailsHandler(
    IProblemDetailsService problemDetails,
    IHostEnvironment environment,
    ILogger<ExceptionToProblemDetailsHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception ex, CancellationToken ct)
    {
        var (status, title) = ex switch
        {
            EntityNotFoundException => (StatusCodes.Status404NotFound, "Ресурс не знайдено"),
            ValidationException => (StatusCodes.Status400BadRequest, "Некоректні дані"),
            ConflictException => (StatusCodes.Status409Conflict, "Конфлікт даних"),
            DomainException => (StatusCodes.Status409Conflict, "Порушено бізнес-правило"),
            _ => (StatusCodes.Status500InternalServerError, "Внутрішня помилка сервера")
        };

        if (status >= 500)
            logger.LogError(ex, "Запит {Method} {Path} завершився помилкою", http.Request.Method, http.Request.Path);
        else
            logger.LogWarning("Запит {Method} {Path} відхилено ({Status}): {Reason}",
                http.Request.Method, http.Request.Path, status, ex.Message);

        ProblemDetails problem = ex is ValidationException { Errors.Count: > 0 } validation
            ? new ValidationProblemDetails(validation.Errors.ToDictionary(e => e.Key, e => e.Value))
            : new ProblemDetails();

        problem.Status = status;
        problem.Title = title;
        problem.Instance = http.Request.Path;
        problem.Detail = status < 500
            ? ex.Message
            : environment.IsDevelopment() ? ex.ToString() : "Спробуйте пізніше.";   // деталі — лише в Development

        http.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails = problem,
            Exception = ex
        });
    }
}
