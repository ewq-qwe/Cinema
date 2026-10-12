using Booking.Domain.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Booking.Api.Errors;

/// <summary>
/// Єдиний глобальний обробник: доменний виняток → HTTP-статус у форматі ProblemDetails (RFC 9457).
/// Завершальна ланка ланцюжка «процедура (THROW 500xx) → DAL → BLL → HTTP-статус».
/// Контролери не містять try/catch.
/// </summary>
public sealed class DomainExceptionHandler(IProblemDetailsService problemDetails, ILogger<DomainExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception ex, CancellationToken ct)
    {
        var (status, title) = ex switch
        {
            NotFoundException => (StatusCodes.Status404NotFound, "Ресурс не знайдено"),
            BusinessConflictException => (StatusCodes.Status409Conflict, "Конфлікт із бізнес-правилами"),
            ValidationException => (StatusCodes.Status400BadRequest, "Некоректні дані"),
            _ => (StatusCodes.Status500InternalServerError, "Внутрішня помилка сервера")
        };

        if (status == StatusCodes.Status500InternalServerError)
            logger.LogError(ex, "Необроблена помилка запиту {Path}", http.Request.Path);
        else
            logger.LogInformation("{Status}: {Message}", status, ex.Message);

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            // Для 500 не розкриваємо внутрішні подробиці (сирі SQL-повідомлення тощо)
            Detail = status == StatusCodes.Status500InternalServerError ? "Спробуйте пізніше." : ex.Message,
            Instance = http.Request.Path
        };
        if (ex is ValidationException { Errors.Count: > 0 } validation)
            problem.Extensions["errors"] = validation.Errors;

        http.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails = problem,
            Exception = ex
        });
    }
}
