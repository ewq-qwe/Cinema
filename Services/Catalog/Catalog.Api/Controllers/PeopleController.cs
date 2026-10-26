using Catalog.Bll.Dtos;
using Catalog.Bll.Services;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace Catalog.Api.Controllers;

/// <summary>Люди (актори, режисери) і їхня участь у фільмах.</summary>
[ApiController]
[Route("api/people")]
[Produces("application/json")]
public sealed class PeopleController(IPersonService people) : ControllerBase
{
    /// <summary>Список людей: пагінація, пошук за іменем, фільтр за роком народження, сортування.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<PersonDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<PersonDto>>> GetList(
        [FromQuery] PersonQuery query, [FromServices] IValidator<PersonQuery> validator, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(query, ct);
        if (!validation.IsValid) return this.ValidationProblem(validation);

        var page = await people.GetListAsync(query, ct);
        Response.AddPaginationHeaders(page);
        return Ok(page);
    }

    /// <summary>Людина за Id.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType<PersonDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PersonDto>> GetById(int id, CancellationToken ct) =>
        Ok(await people.GetByIdAsync(id, ct));

    /// <summary>Додати людину.</summary>
    [HttpPost]
    [ProducesResponseType<PersonDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PersonDto>> Create(
        CreatePersonDto input, [FromServices] IValidator<CreatePersonDto> validator, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(input, ct);
        if (!validation.IsValid) return this.ValidationProblem(validation);

        var created = await people.CreateAsync(input, ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    /// <summary>Фільмографія (LINQ через проміжну сутність MovieCredit).</summary>
    [HttpGet("{id:int}/filmography")]
    [ProducesResponseType<IReadOnlyList<FilmographyItemDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<FilmographyItemDto>>> GetFilmography(int id, CancellationToken ct) =>
        Ok(await people.GetFilmographyAsync(id, ct));

    /// <summary>Актори головних ролей (позиція в титрах ≤ maxBillingOrder), за потреби — лише в жанрі.</summary>
    [HttpGet("leads")]
    [ProducesResponseType<IReadOnlyList<PersonDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<PersonDto>>> GetLeadActors(
        [FromQuery] string? genre, [FromQuery] int maxBillingOrder = 1, CancellationToken ct = default) =>
        Ok(await people.GetLeadActorsAsync(genre, maxBillingOrder, ct));
}
