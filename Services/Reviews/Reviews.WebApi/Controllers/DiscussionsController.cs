using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using Reviews.Application.Common.Models;
using Reviews.Application.Discussions.Commands.CreateDiscussion;
using Reviews.Application.Discussions.Commands.PostMessage;
using Reviews.Application.Discussions.Queries.GetDiscussions;
using Reviews.WebApi.Contracts;

namespace Reviews.WebApi.Controllers;

/// <summary>Обговорення фільмів (колекції discussions і messages).</summary>
[Route("api/discussions")]
public sealed class DiscussionsController(IMapper mapper) : ApiControllerBase
{
    /// <summary>Стрічка тем: закріплені й найактивніші зверху; останні повідомлення вже вбудовано.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<DiscussionDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<DiscussionDto>>> GetList(
        [FromQuery] int? movieId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) =>
        Ok(await Mediator.Send(new GetDiscussionsListQuery(movieId, page, pageSize), ct));

    [HttpGet("{id}", Name = nameof(GetDiscussion))]
    [ProducesResponseType<DiscussionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DiscussionDto>> GetDiscussion(string id, CancellationToken ct)
    {
        var discussion = await Mediator.Send(new GetDiscussionByIdQuery(id), ct);
        SetETag(discussion.Version);
        return Ok(discussion);
    }

    /// <summary>Створити тему. 201 + Location з рядковим ObjectId.</summary>
    [HttpPost]
    [ProducesResponseType<string>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<string>> Create(CreateDiscussionCommand command, CancellationToken ct)
    {
        var id = await Mediator.Send(command, ct);
        return CreatedAtAction(nameof(GetDiscussion), new { id }, id);
    }

    /// <summary>Повідомлення теми — курсорна пагінація: after = nextCursor попередньої сторінки.</summary>
    [HttpGet("{id}/messages", Name = nameof(GetMessages))]
    [ProducesResponseType<CursorPage<MessageDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CursorPage<MessageDto>>> GetMessages(
        string id, [FromQuery] string? after, [FromQuery] int limit = 20, CancellationToken ct = default) =>
        Ok(await Mediator.Send(new GetDiscussionMessagesQuery(id, after, limit), ct));

    /// <summary>Написати повідомлення (документ у messages + прев'ю в темі).</summary>
    [HttpPost("{id}/messages")]
    [ProducesResponseType<MessageDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MessageDto>> PostMessage(string id, PostMessageRequest request, CancellationToken ct)
    {
        var message = await Mediator.Send(mapper.Map<PostMessageCommand>(request) with { DiscussionId = id }, ct);
        return CreatedAtAction(nameof(GetMessages), new { id }, message);
    }
}
