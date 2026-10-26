using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using Reviews.Domain.Exceptions;
using ValidationException = FluentValidation.ValidationException;

namespace Reviews.WebApi.Errors;

/// <summary>
/// Глобальний обробник: доменні, валідаційні та MongoDB-винятки → ProblemDetails з правильним HTTP-статусом.
/// NotFound → 404 · Validation → 400 (помилки по полях) · Domain → 400 · Conflict → 409 ·
/// DuplicateKey → 409 · інші помилки запису → 400 · немає з'єднання з MongoDB → 503 · решта → 500.
/// Кожен виняток логується один раз — тут.
/// </summary>
public sealed partial class ExceptionToProblemDetailsHandler(
    IProblemDetailsService problemDetails,
    IHostEnvironment environment,
    ILogger<ExceptionToProblemDetailsHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception ex, CancellationToken ct)
    {
        var (status, title, detail) = ex switch
        {
            NotFoundException => (StatusCodes.Status404NotFound, "Ресурс не знайдено", ex.Message),
            ValidationException => (StatusCodes.Status400BadRequest, "Некоректні дані", "Перевірте поля в errors."),
            ConflictException => (StatusCodes.Status409Conflict, "Конфлікт версій", ex.Message),
            DomainException => (StatusCodes.Status400BadRequest, "Порушено бізнес-правило", ex.Message),

            MongoWriteException { WriteError.Category: ServerErrorCategory.DuplicateKey } w =>
                (StatusCodes.Status409Conflict, "Дублікат", DuplicateMessage(w.WriteError.Message)),
            MongoBulkWriteException b when b.WriteErrors.Any(e => e.Category == ServerErrorCategory.DuplicateKey) =>
                (StatusCodes.Status409Conflict, "Дублікат", DuplicateMessage(b.WriteErrors[0].Message)),
            MongoWriteException w =>
                (StatusCodes.Status400BadRequest, "Документ відхилено базою даних", w.WriteError.Message),

            MongoConnectionException or TimeoutException =>
                (StatusCodes.Status503ServiceUnavailable, "База даних недоступна", "MongoDB тимчасово недоступна. Спробуйте пізніше."),

            _ => (StatusCodes.Status500InternalServerError, "Внутрішня помилка сервера",
                environment.IsDevelopment() ? ex.ToString() : "Спробуйте пізніше.")   // деталі — лише в Development
        };

        if (status >= 500)
            logger.LogError(ex, "Запит {Method} {Path} завершився помилкою {Status} ({ExceptionType})",
                http.Request.Method, http.Request.Path, status, ex.GetType().Name);
        else
            logger.LogWarning("Запит {Method} {Path} відхилено ({Status}, {ExceptionType}): {Reason}",
                http.Request.Method, http.Request.Path, status, ex.GetType().Name, ex.Message);

        ProblemDetails problem = ex is ValidationException validation
            ? new ValidationProblemDetails(validation.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray()))
            : new ProblemDetails();

        problem.Status = status;
        problem.Title = title;
        problem.Detail = detail;
        problem.Instance = http.Request.Path;

        if (status == StatusCodes.Status503ServiceUnavailable)
            http.Response.Headers.RetryAfter = "10";

        http.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails = problem,
            Exception = ex
        });
    }

    /// <summary>Назва індексу з повідомлення сервера: "E11000 duplicate key error ... index: ux_reviews_movie_author dup key: …".</summary>
    private static string DuplicateMessage(string serverMessage)
    {
        var index = IndexName().Match(serverMessage);
        return index.Success
            ? $"Документ з такими даними вже існує (унікальний індекс {index.Groups[1].Value})."
            : "Документ з такими даними вже існує.";
    }

    [GeneratedRegex(@"index: (\S+)")]
    private static partial Regex IndexName();
}
