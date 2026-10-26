using Catalog.Bll.Dtos;
using Catalog.Bll.Services;
using Microsoft.AspNetCore.Mvc;

namespace Catalog.Api.Controllers;

/// <summary>Жанри (довідник).</summary>
[ApiController]
[Route("api/genres")]
[Produces("application/json")]
public sealed class GenresController(IGenreService genres) : ControllerBase
{
    /// <summary>Усі жанри.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<GenreDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<GenreDto>>> GetAll(CancellationToken ct) =>
        Ok(await genres.GetAllAsync(ct));

    /// <summary>Жанр з його фільмами (explicit loading: два окремі запити).</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType<GenreWithMoviesDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GenreWithMoviesDto>> GetById(int id, CancellationToken ct) =>
        Ok(await genres.GetWithMoviesAsync(id, ct));

    /// <summary>Додати жанр. Дубль назви → 409.</summary>
    [HttpPost]
    [ProducesResponseType<GenreDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<GenreDto>> Create(CreateGenreDto input, CancellationToken ct)
    {
        var created = await genres.CreateAsync(input, ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    /// <summary>Видалити жанр без фільмів.</summary>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await genres.DeleteAsync(id, ct);
        return NoContent();
    }
}
