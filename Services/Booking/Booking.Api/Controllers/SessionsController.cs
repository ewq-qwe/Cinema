using Booking.Bll.Dtos;
using Booking.Bll.Services;
using Microsoft.AspNetCore.Mvc;

namespace Booking.Api.Controllers;

/// <summary>Сеанси (розклад): CRUD і схема залу.</summary>
[ApiController]
[Route("api/sessions")]
[Produces("application/json")]
public sealed class SessionsController(ISessionService sessions) : ControllerBase
{
    /// <summary>Майбутні сеанси (за потреби — лише одного фільму).</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<SessionDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SessionDto>>> GetUpcoming([FromQuery] long? movieId, CancellationToken ct)
        => Ok(await sessions.GetUpcomingAsync(movieId, ct));

    /// <summary>Сеанс за Id.</summary>
    [HttpGet("{id:long}")]
    [ProducesResponseType<SessionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SessionDto>> GetById(long id, CancellationToken ct)
        => Ok(await sessions.GetAsync(id, ct));

    /// <summary>Схема залу: вільні й зайняті місця на сеансі.</summary>
    [HttpGet("{id:long}/seats")]
    [ProducesResponseType<IReadOnlyList<SeatDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<SeatDto>>> GetSeats(long id, CancellationToken ct)
        => Ok(await sessions.GetSeatMapAsync(id, ct));

    /// <summary>Запланувати сеанс.</summary>
    [HttpPost]
    [ProducesResponseType<SessionDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SessionDto>> Create(SaveSessionDto input, CancellationToken ct)
    {
        var created = await sessions.CreateAsync(input, ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    /// <summary>Змінити сеанс (якщо квитки вже продано — лише ціну).</summary>
    [HttpPut("{id:long}")]
    [ProducesResponseType<SessionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SessionDto>> Update(long id, SaveSessionDto input, CancellationToken ct)
        => Ok(await sessions.UpdateAsync(id, input, ct));

    /// <summary>Видалити сеанс без бронювань.</summary>
    [HttpDelete("{id:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        await sessions.DeleteAsync(id, ct);
        return NoContent();
    }
}
