using Microsoft.AspNetCore.Mvc;
using Platform.Aggregator.Caching;
using Platform.Aggregator.Clients;
using Platform.Aggregator.Models;
using Platform.Aggregator.Services;

namespace Platform.Aggregator.Controllers;

/// <summary>
/// Композитні запити: одна відповідь з даних кількох мікросервісів. Для зовнішніх клієнтів (через Gateway) —
/// звичайний HTTP; до сервісів агрегатор ходить по gRPC (Лаб. 06). Готова повна відповідь кешується в Redis на 30 с.
/// </summary>
[ApiController]
[Route("api/aggregator")]
[Produces("application/json")]
public sealed class AggregatorController(
    [FromKeyedServices(Transports.Grpc)] IAggregationService grpc,
    [FromKeyedServices(Transports.Http)] IAggregationService http,
    AggregatedResponseCache cache) : ControllerBase
{
    /// <summary>
    /// Повна картка бронювання: бронювання й сеанс (booking-api), фільм (catalog-api), рейтинг і останні відгуки (reviews-api).
    /// Якщо необов'язковий сервіс недоступний — часткова відповідь з попередженням у warnings (вона не кешується).
    /// </summary>
    /// <param name="id">Id бронювання.</param>
    /// <param name="transport">grpc (за замовчуванням) або http — для порівняння затримок із Лаб. 05.</param>
    /// <param name="useCache">false — оминути кеш агрегату (для вимірювань).</param>
    /// <param name="ct">Скасування запиту.</param>
    [HttpGet("bookings/{id:long}/full")]
    [ProducesResponseType<BookingCardDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public Task<IActionResult> GetBookingCard(long id, [FromQuery] string transport = Transports.Grpc,
        [FromQuery] bool useCache = true, CancellationToken ct = default) =>
        Aggregate(AggregatedResponseCache.BookingKey(id), useCache,
            () => Pick(transport).GetBookingCardAsync(id, ct), card => card.Warnings, $"Бронювання {id} не знайдено.", ct);

    /// <summary>Картка фільму: фільм (catalog-api), найближчі сеанси (booking-api), рейтинг і відгуки (reviews-api).</summary>
    /// <param name="id">Id фільму в Каталозі.</param>
    /// <param name="transport">grpc (за замовчуванням) або http.</param>
    /// <param name="useCache">false — оминути кеш агрегату.</param>
    /// <param name="ct">Скасування запиту.</param>
    [HttpGet("movies/{id:int}/full")]
    [ProducesResponseType<MovieCardDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public Task<IActionResult> GetMovieCard(int id, [FromQuery] string transport = Transports.Grpc,
        [FromQuery] bool useCache = true, CancellationToken ct = default) =>
        Aggregate(AggregatedResponseCache.MovieKey(id), useCache,
            () => Pick(transport).GetMovieCardAsync(id, ct), card => card.Warnings, $"Фільм {id} не знайдено в Каталозі.", ct);

    private IAggregationService Pick(string transport) =>
        string.Equals(transport, Transports.Http, StringComparison.OrdinalIgnoreCase) ? http : grpc;

    private async Task<IActionResult> Aggregate<T>(string key, bool useCache, Func<Task<T?>> aggregate,
        Func<T, IReadOnlyList<string>> warnings, string notFound, CancellationToken ct) where T : class
    {
        // Кеш перевіряється ДО паралельних викликів сервісів
        if (useCache && await cache.GetAsync<T>(key, ct) is { } cached)
        {
            Response.Headers["X-Aggregator-Cache"] = "HIT";
            return Ok(cached);
        }
        Response.Headers["X-Aggregator-Cache"] = useCache ? "MISS" : "BYPASS";

        try
        {
            var card = await aggregate();
            if (card is null)
                return Problem(statusCode: StatusCodes.Status404NotFound, title: "Ресурс не знайдено", detail: notFound);

            if (useCache) await cache.SetAsync(key, card, warnings(card), ct);   // часткові відповіді не кешуються
            return Ok(card);
        }
        catch (UpstreamUnavailableException ex)
        {
            Response.Headers.RetryAfter = "10";
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Основний сервіс недоступний", detail: ex.Message);
        }
    }
}
