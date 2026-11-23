using Booking.Api.Metrics;
using Booking.Bll.Dtos;
using Booking.Bll.Services;
using Microsoft.AspNetCore.Mvc;

namespace Booking.Api.Controllers;

/// <summary>Бронювання квитків: створення, оплата, скасування, вхід у зал.</summary>
[ApiController]
[Route("api/bookings")]
[Produces("application/json")]
// Лаб. 07: операції запису вимірюються (bookings.operation.duration) і рахуються бізнес-лічильниками.
public sealed class BookingsController(IBookingService bookings, BookingMetrics metrics) : ControllerBase
{
    /// <summary>Бронювання з квитками та оплатою.</summary>
    [HttpGet("{id:long}")]
    [ProducesResponseType<BookingDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BookingDto>> GetById(long id, CancellationToken ct)
        => Ok(await bookings.GetAsync(id, ct));

    /// <summary>Забронювати місця на сеанс (транзакція: бронювання + квитки, ціни — знімок).</summary>
    [HttpPost]
    [ProducesResponseType<BookingDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BookingDto>> Create(CreateBookingDto input, CancellationToken ct)
    {
        var created = await metrics.MeasureAsync("create", () => bookings.CreateAsync(input, ct));
        metrics.BookingCreated(created.Status, created.Tickets.Count);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    /// <summary>Оплатити (транзакція: статус бронювання + запис оплати). Повтор з тим самим TransactionId — без подвійної оплати.</summary>
    [HttpPost("{id:long}/pay")]
    [ProducesResponseType<BookingDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BookingDto>> Pay(long id, PayBookingDto input, CancellationToken ct)
    {
        var paid = await metrics.MeasureAsync("pay", () => bookings.PayAsync(id, input, ct));
        metrics.BookingPaid(input.Method);
        return Ok(paid);
    }

    /// <summary>Скасувати бронювання (лише до початку сеансу).</summary>
    [HttpPost("{id:long}/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(long id, CancelBookingDto input, CancellationToken ct)
    {
        await metrics.MeasureAsync("cancel", () => bookings.CancelAsync(id, input, ct));
        metrics.BookingCancelled();
        return NoContent();
    }

    /// <summary>Вхід у зал: Paid → Used. Неоплачене або вже використане бронювання → 409.</summary>
    [HttpPost("{id:long}/check-in")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CheckIn(long id, CancellationToken ct)
    {
        await metrics.MeasureAsync("check-in", () => bookings.CheckInAsync(id, ct));
        metrics.BookingCheckedIn();
        return NoContent();
    }
}
