using MediatR;
using Reviews.Application.Common.Interfaces;
using Reviews.Application.Common.Models;
using Reviews.Application.Common.Validation;

namespace Reviews.Application.Reviews.Queries.GetReviewsList;

public sealed class GetReviewsListQueryHandler(IReviewQueries queries)
    : IRequestHandler<GetReviewsListQuery, PagedResult<ReviewDto>>
{
    public const int MaxPageSize = 100;

    public Task<PagedResult<ReviewDto>> Handle(GetReviewsListQuery q, CancellationToken ct)
    {
        var filter = new ReviewListFilter(
            q.MovieId, q.AuthorId,
            q.Kind is null ? null : ReviewKinds.Parse(q.Kind),
            q.MinRating,
            string.IsNullOrWhiteSpace(q.Search) ? null : q.Search.Trim(),
            q.SortBy);

        return queries.GetListAsync(filter, q.Page, Math.Clamp(q.PageSize, 1, MaxPageSize), ct);
    }
}
