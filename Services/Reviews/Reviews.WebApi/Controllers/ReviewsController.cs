using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using Reviews.Application.Common.Interfaces;
using Reviews.Application.Common.Models;
using Reviews.Application.Reviews.Commands.AddComment;
using Reviews.Application.Reviews.Commands.CreateReview;
using Reviews.Application.Reviews.Commands.DeleteReview;
using Reviews.Application.Reviews.Commands.UpdateReview;
using Reviews.Application.Reviews.Queries.GetReviewById;
using Reviews.Application.Reviews.Queries.GetReviewsList;
using Reviews.WebApi.Contracts;

namespace Reviews.WebApi.Controllers;

/// <summary>Відгуки на фільми (колекція reviews).</summary>
[Route("api/reviews")]
public sealed class ReviewsController(IMapper mapper) : ApiControllerBase
{
    /// <summary>Список відгуків: фільтри, повнотекстовий пошук, сортування, пагінація (skip/limit у MongoDB).</summary>
    /// <param name="movieId">Фільм (Movies.Id з Каталогу).</param>
    /// <param name="authorId">Глядач (Customers.Id з Бронювання).</param>
    /// <param name="kind">quick | full | critic.</param>
    /// <param name="minRating">Мінімальна оцінка 1..5.</param>
    /// <param name="search">Пошук за заголовком і текстом (text-індекс).</param>
    /// <param name="sortBy">newest | oldest | rating | likes.</param>
    /// <param name="page">Номер сторінки з 1.</param>
    /// <param name="pageSize">Розмір сторінки 1..100.</param>
    /// <param name="ct">Скасування запиту.</param>
    [HttpGet]
    [ProducesResponseType<PagedResult<ReviewDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<ReviewDto>>> GetList(
        [FromQuery] int? movieId, [FromQuery] int? authorId, [FromQuery] string? kind, [FromQuery] int? minRating,
        [FromQuery] string? search, [FromQuery] string sortBy = ReviewSort.Newest,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await Mediator.Send(new GetReviewsListQuery(movieId, authorId, kind, minRating, search, sortBy, page, pageSize), ct);
        Response.Headers["X-Pagination-TotalCount"] = result.TotalCount.ToString();
        return Ok(result);
    }

    /// <summary>Відгук із вбудованими коментарями. Версія документа — у заголовку ETag.</summary>
    [HttpGet("{id}", Name = nameof(GetById))]
    [ProducesResponseType<ReviewDetailsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReviewDetailsDto>> GetById(string id, CancellationToken ct)
    {
        var review = await Mediator.Send(new GetReviewByIdQuery(id), ct);
        SetETag(review.Version);
        return Ok(review);
    }

    /// <summary>Створити відгук. 201 + Location: /api/reviews/{ObjectId}.</summary>
    [HttpPost]
    [ProducesResponseType<string>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<string>> Create(CreateReviewCommand command, CancellationToken ct)
    {
        var id = await Mediator.Send(command, ct);
        SetETag(0);
        return CreatedAtAction(nameof(GetById), new { id }, id);
    }

    /// <summary>Редагувати відгук. Обов'язковий If-Match з версією; чужа зміна → 409.</summary>
    [HttpPut("{id}")]
    [ProducesResponseType<ReviewDetailsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status428PreconditionRequired)]
    public async Task<ActionResult<ReviewDetailsDto>> Update(
        string id, UpdateReviewRequest request, [FromHeader(Name = "If-Match")] string? ifMatch, CancellationToken ct)
    {
        if (ParseIfMatch(ifMatch) is not { } version) return PreconditionRequired();

        var command = mapper.Map<UpdateReviewCommand>(request) with { Id = id, ExpectedVersion = version };
        var review = await Mediator.Send(command, ct);
        SetETag(review.Version);
        return Ok(review);
    }

    /// <summary>Видалити відгук. If-Match необов'язковий: якщо є — видаляється лише ця версія.</summary>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(string id, [FromHeader(Name = "If-Match")] string? ifMatch, CancellationToken ct)
    {
        await Mediator.Send(new DeleteReviewCommand(id, ParseIfMatch(ifMatch)), ct);
        return NoContent();
    }

    /// <summary>Додати коментар (вкладений масив comments[]). Лише для повних відгуків.</summary>
    [HttpPost("{id}/comments")]
    [ProducesResponseType<ReviewDetailsDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReviewDetailsDto>> AddComment(string id, AddCommentRequest request, CancellationToken ct)
    {
        var review = await Mediator.Send(mapper.Map<AddCommentCommand>(request) with { ReviewId = id }, ct);
        SetETag(review.Version);
        return CreatedAtAction(nameof(GetById), new { id }, review);
    }
}
