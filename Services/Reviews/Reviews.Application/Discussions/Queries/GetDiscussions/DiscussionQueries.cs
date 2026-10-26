using FluentValidation;
using MediatR;
using MongoDB.Bson;
using Reviews.Application.Common.Interfaces;
using Reviews.Application.Common.Messaging;
using Reviews.Application.Common.Models;
using Reviews.Application.Common.Validation;
using Reviews.Domain.Exceptions;
using Reviews.Domain.Interfaces;

namespace Reviews.Application.Discussions.Queries.GetDiscussions;

// ---------- Тема за Id ----------
public sealed record GetDiscussionByIdQuery(string Id) : IQuery<DiscussionDto>;

public sealed class GetDiscussionByIdQueryHandler(IDiscussionQueries queries) : IRequestHandler<GetDiscussionByIdQuery, DiscussionDto>
{
    public async Task<DiscussionDto> Handle(GetDiscussionByIdQuery q, CancellationToken ct) =>
        await queries.GetByIdAsync(ObjectId.Parse(q.Id), ct) ?? throw NotFoundException.For("Обговорення", q.Id);
}

public sealed class GetDiscussionByIdQueryValidator : AbstractValidator<GetDiscussionByIdQuery>
{
    public GetDiscussionByIdQueryValidator() => RuleFor(x => x.Id).MustBeObjectId();
}

// ---------- Стрічка тем фільму (закріплені й активні зверху) ----------
public sealed record GetDiscussionsListQuery(int? MovieId = null, int Page = 1, int PageSize = 20)
    : IQuery<PagedResult<DiscussionDto>>;

public sealed class GetDiscussionsListQueryHandler(IDiscussionQueries queries)
    : IRequestHandler<GetDiscussionsListQuery, PagedResult<DiscussionDto>>
{
    public Task<PagedResult<DiscussionDto>> Handle(GetDiscussionsListQuery q, CancellationToken ct) =>
        queries.GetListAsync(q.MovieId, q.Page, Math.Clamp(q.PageSize, 1, 100), ct);
}

public sealed class GetDiscussionsListQueryValidator : AbstractValidator<GetDiscussionsListQuery>
{
    public GetDiscussionsListQueryValidator()
    {
        RuleFor(x => x.MovieId).GreaterThan(0).When(x => x.MovieId.HasValue);
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}

// ---------- Повідомлення теми: курсорна пагінація ----------
public sealed record GetDiscussionMessagesQuery(string DiscussionId, string? After = null, int Limit = 20)
    : IQuery<CursorPage<MessageDto>>;

public sealed class GetDiscussionMessagesQueryHandler(IDiscussionRepository discussions, IDiscussionQueries queries)
    : IRequestHandler<GetDiscussionMessagesQuery, CursorPage<MessageDto>>
{
    public async Task<CursorPage<MessageDto>> Handle(GetDiscussionMessagesQuery q, CancellationToken ct)
    {
        var discussionId = ObjectId.Parse(q.DiscussionId);
        if (!await discussions.ExistsAsync(discussionId, ct))
            throw NotFoundException.For("Обговорення", q.DiscussionId);

        return await queries.GetMessagesAsync(discussionId, q.After is null ? null : ObjectId.Parse(q.After), q.Limit, ct);
    }
}

public sealed class GetDiscussionMessagesQueryValidator : AbstractValidator<GetDiscussionMessagesQuery>
{
    public GetDiscussionMessagesQueryValidator()
    {
        RuleFor(x => x.DiscussionId).MustBeObjectId();
        RuleFor(x => x.After).MustBeObjectIdIfPresent();
        RuleFor(x => x.Limit).InclusiveBetween(1, 100);
    }
}
