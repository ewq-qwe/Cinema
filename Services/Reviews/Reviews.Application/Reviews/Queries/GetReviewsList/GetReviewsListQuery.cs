using Reviews.Application.Common.Interfaces;
using Reviews.Application.Common.Messaging;
using Reviews.Application.Common.Models;

namespace Reviews.Application.Reviews.Queries.GetReviewsList;

/// <summary>
/// Список відгуків з фільтрами, повнотекстовим пошуком (text-індекс), сортуванням і пагінацією skip/limit.
/// </summary>
public sealed record GetReviewsListQuery(
    int? MovieId = null,
    int? AuthorId = null,
    string? Kind = null,
    int? MinRating = null,
    string? Search = null,
    string SortBy = ReviewSort.Newest,
    int Page = 1,
    int PageSize = 20) : IQuery<PagedResult<ReviewDto>>;
