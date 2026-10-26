using MongoDB.Bson;
using Reviews.Application.Common.Models;
using Reviews.Domain.Enums;

namespace Reviews.Application.Common.Interfaces;

/// <summary>Критерії списку відгуків (перетворюються на $match в Infrastructure).</summary>
public sealed record ReviewListFilter(
    int? MovieId = null,
    int? AuthorId = null,
    ReviewKind? Kind = null,
    int? MinRating = null,
    string? Search = null,
    string SortBy = ReviewSort.Newest);

public static class ReviewSort
{
    public const string Newest = "newest";
    public const string Oldest = "oldest";
    public const string Rating = "rating";
    public const string Likes = "likes";
    public static readonly string[] All = [Newest, Oldest, Rating, Likes];
}

/// <summary>
/// Сторона читання CQRS (як IOrderQueries в eShop). Обробники запитів не знають про колекції MongoDB:
/// реалізація в Infrastructure робить проєкцію одразу в DTO, фільтрацію й пагінацію засобами MongoDB.
/// </summary>
public interface IReviewQueries
{
    Task<ReviewDetailsDto?> GetByIdAsync(ObjectId id, CancellationToken ct);
    Task<PagedResult<ReviewDto>> GetListAsync(ReviewListFilter filter, int page, int pageSize, CancellationToken ct);
    Task<MovieRatingDto?> GetMovieRatingAsync(int movieId, CancellationToken ct);
}

public interface IDiscussionQueries
{
    Task<DiscussionDto?> GetByIdAsync(ObjectId id, CancellationToken ct);
    Task<PagedResult<DiscussionDto>> GetListAsync(int? movieId, int page, int pageSize, CancellationToken ct);

    /// <summary>Курсорна пагінація повідомлень: наступні <paramref name="limit"/> після <paramref name="after"/>.</summary>
    Task<CursorPage<MessageDto>> GetMessagesAsync(ObjectId discussionId, ObjectId? after, int limit, CancellationToken ct);
}
