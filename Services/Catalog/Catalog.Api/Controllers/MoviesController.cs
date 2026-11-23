using Catalog.Api.Metrics;
using Catalog.Bll.Dtos;
using Catalog.Bll.Services;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace Catalog.Api.Controllers;

/// <summary>Фільми каталогу. Тонкий адаптер: валідація вводу + виклик сервісу; без DbContext і бізнес-правил.</summary>
[ApiController]
[Route("api/movies")]
[Produces("application/json")]
// Лаб. 07: операції вимірюються (catalog.operation.duration) і рахуються бізнес-лічильниками (CatalogMetrics).
public sealed class MoviesController(IMovieService movies, CatalogMetrics metrics) : ControllerBase
{
    /// <summary>Список фільмів: пагінація, фільтри (назва, жанр, роки, рейтинг) і сортування — виконуються в SQL.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<MovieListItemDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<MovieListItemDto>>> GetList(
        [FromQuery] MovieQuery query, [FromServices] IValidator<MovieQuery> validator, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(query, ct);
        if (!validation.IsValid) return this.ValidationProblem(validation);

        var page = await movies.GetListAsync(query, ct);
        Response.AddPaginationHeaders(page);
        return Ok(page);
    }

    /// <summary>Картка фільму з описом і титрами (eager loading).</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType<MovieDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MovieDto>> GetById(int id, CancellationToken ct)
    {
        metrics.CardRequested("rest");
        return Ok(await metrics.MeasureAsync("get-card", () => movies.GetByIdAsync(id, ct)));
    }

    /// <summary>Додати фільм. Дубль «назва + рік» → 409.</summary>
    [HttpPost]
    [ProducesResponseType<MovieDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<MovieDto>> Create(
        CreateMovieDto input, [FromServices] IValidator<CreateMovieDto> validator, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(input, ct);
        if (!validation.IsValid) return this.ValidationProblem(validation);

        var created = await metrics.MeasureAsync("create", () => movies.CreateAsync(input, ct));
        metrics.MovieCreated(created.AgeRating);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    /// <summary>Змінити фільм (потрібен RowVersion з GET — оптимістична конкурентність).</summary>
    [HttpPut("{id:int}")]
    [ProducesResponseType<MovieDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<MovieDto>> Update(
        int id, UpdateMovieDto input, [FromServices] IValidator<UpdateMovieDto> validator, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(input, ct);
        if (!validation.IsValid) return this.ValidationProblem(validation);
        var updated = await metrics.MeasureAsync("update", () => movies.UpdateAsync(id, input, ct));
        metrics.MovieUpdated("details");
        return Ok(updated);
    }

    /// <summary>Створити або оновити опис фільму (1:1).</summary>
    [HttpPut("{id:int}/detail")]
    [ProducesResponseType<MovieDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MovieDto>> SetDetail(
        int id, SetMovieDetailDto input, [FromServices] IValidator<SetMovieDetailDto> validator, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(input, ct);
        if (!validation.IsValid) return this.ValidationProblem(validation);
        var updated = await metrics.MeasureAsync("set-detail", () => movies.SetDetailAsync(id, input, ct));
        metrics.MovieUpdated("description");
        return Ok(updated);
    }

    /// <summary>Додати людину в титри (M:N з власними полями). Порушення правил титрів → 409.</summary>
    [HttpPost("{id:int}/credits")]
    [ProducesResponseType<MovieDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<MovieDto>> AddCredit(
        int id, AddCreditDto input, [FromServices] IValidator<AddCreditDto> validator, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(input, ct);
        if (!validation.IsValid) return this.ValidationProblem(validation);
        var updated = await metrics.MeasureAsync("add-credit", () => movies.AddCreditAsync(id, input, ct));
        metrics.MovieUpdated("credits");
        return Ok(updated);
    }

    /// <summary>Прибрати людину з титрів.</summary>
    [HttpDelete("{id:int}/credits/{personId:int}/{role}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RemoveCredit(int id, int personId, string role, CancellationToken ct)
    {
        await metrics.MeasureAsync("remove-credit", () => movies.RemoveCreditAsync(id, personId, role, ct));
        metrics.MovieUpdated("credits");
        return NoContent();
    }

    /// <summary>Перенести фільм в архів (м'яке видалення).</summary>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Archive(int id, CancellationToken ct)
    {
        await metrics.MeasureAsync("archive", () => movies.ArchiveAsync(id, ct));
        metrics.MovieUpdated("archive");
        return NoContent();
    }
}
