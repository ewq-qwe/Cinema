using Microsoft.AspNetCore.Mvc;
using Platform.Aggregator.Models;
using Platform.Aggregator.Services;

namespace Platform.Aggregator.Controllers;

/// <summary>Композитні запити: одна відповідь з даних кількох мікросервісів.</summary>
[ApiController]
[Route("api/aggregator")]
[Produces("application/json")]
public sealed class AggregatorController(IAggregationService aggregation) : ControllerBase
{
    /// <summary>
    /// Повна картка бронювання: бронювання й сеанс (booking-api), фільм (catalog-api), рейтинг і останні відгуки (reviews-api).
    /// Якщо необов'язковий сервіс недоступний — часткова відповідь з попередженням у warnings.
    /// </summary>
    [HttpGet("bookings/{id:long}/full")]
    [ProducesResponseType<BookingCardDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public Task<IActionResult> GetBookingCard(long id, CancellationToken ct) =>
        Aggregate(() => aggregation.GetBookingCardAsync(id, ct), $"Бронювання {id} не знайдено.");

    /// <summary>Картка фільму: фільм (catalog-api), найближчі сеанси (booking-api), рейтинг і відгуки (reviews-api).</summary>
    [HttpGet("movies/{id:int}/full")]
    [ProducesResponseType<MovieCardDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public Task<IActionResult> GetMovieCard(int id, CancellationToken ct) =>
        Aggregate(() => aggregation.GetMovieCardAsync(id, ct), $"Фільм {id} не знайдено в Каталозі.");

    private async Task<IActionResult> Aggregate<T>(Func<Task<T?>> aggregate, string notFound) where T : class
    {
        try
        {
            return await aggregate() is { } card ? Ok(card) : Problem(statusCode: StatusCodes.Status404NotFound, title: "Ресурс не знайдено", detail: notFound);
        }
        catch (UpstreamUnavailableException ex)
        {
            Response.Headers.RetryAfter = "10";
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Основний сервіс недоступний", detail: ex.Message);
        }
    }
}
