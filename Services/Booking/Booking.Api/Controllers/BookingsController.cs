using Booking.Bll.Dtos;
using Booking.Bll.Services;
using Microsoft.AspNetCore.Mvc;

namespace Booking.Api.Controllers;

/// <summary>Бронювання квитків: створення, оплата, скасування, вхід у зал.</summary>
[ApiController]
[Route("api/bookings")]
[Produces("application/json")]
public sealed class BookingsController(IBookingService bookings) : ControllerBase
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
        var created = await bookings.CreateAsync(input, ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    /// <summary>Оплатити (транзакція: статус бронювання + запис оплати). Повтор з тим самим TransactionId — без подвійної оплати.</summary>
    [HttpPost("{id:long}/pay")]
    [ProducesResponseType<BookingDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BookingDto>> Pay(long id, PayBookingDto input, CancellationToken ct)
        => Ok(await bookings.PayAsync(id, input, ct));

    /// <summary>Скасувати бронювання (лише до початку сеансу).</summary>
    [HttpPost("{id:long}/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(long id, CancelBookingDto input, CancellationToken ct)
    {
        await bookings.CancelAsync(id, input, ct);
        return NoContent();
    }

    /// <summary>Вхід у зал: Paid → Used. Неоплачене або вже використане бронювання → 409.</summary>
    [HttpPost("{id:long}/check-in")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CheckIn(long id, CancellationToken ct)
    {
        await bookings.CheckInAsync(id, ct);
        return NoContent();
    }
}
