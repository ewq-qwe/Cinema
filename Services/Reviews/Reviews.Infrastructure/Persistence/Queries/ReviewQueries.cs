using MongoDB.Bson;
using MongoDB.Driver;
using Reviews.Application.Common.Interfaces;
using Reviews.Application.Common.Models;
using Reviews.Domain.Entities;

namespace Reviews.Infrastructure.Persistence.Queries;

/// <summary>
/// Сторона читання: з БД вибираються лише потрібні поля — $project одразу у форму ReviewDto
/// (author.displayName → authorName, кількість коментарів рахує сам MongoDB через $size).
/// Фільтрація, сортування й пагінація — засобами MongoDB (aggregation pipeline $match → $facet), не в пам'яті.
/// </summary>
public sealed class ReviewQueries(MongoDbContext context) : IReviewQueries
{
    private static readonly BsonDocument ListProjection = new()
    {
        { "movieId", 1 }, { "movieTitle", 1 },
        { "authorId", "$author.customerId" }, { "authorName", "$author.displayName" },
        { "kind", 1 }, { "rating", 1 }, { "title", 1 }, { "text", 1 }, { "hasSpoilers", 1 }, { "likes", 1 },
        { "commentsCount", new BsonDocument("$size", new BsonDocument("$ifNull", new BsonArray { "$comments", new BsonArray() })) },
        { "createdAt", 1 }, { "updatedAt", 1 }, { "version", 1 }
    };

    private static readonly BsonDocument DetailsProjection = new BsonDocument(ListProjection)
    {
        { "photos", 1 }, { "publication", 1 }, { "pros", 1 }, { "cons", 1 }, { "discussionId", 1 }, { "comments", 1 }
    };

    private readonly IMongoCollection<Review> _reviews = context.Reviews;

    public async Task<ReviewDetailsDto?> GetByIdAsync(ObjectId id, CancellationToken ct) =>
        await _reviews.Find(r => r.Id == id)
            .Project<ReviewDetailsDto>(DetailsProjection)
            .FirstOrDefaultAsync(ct);

    public async Task<PagedResult<ReviewDto>> GetListAsync(ReviewListFilter filter, int page, int pageSize, CancellationToken ct)
    {
        var items = AggregateFacet.Create("items", PipelineDefinition<Review, ReviewDto>.Create(
            new BsonDocument("$sort", SortFor(filter.SortBy)),
            new BsonDocument("$skip", (page - 1) * pageSize),
            new BsonDocument("$limit", pageSize),
            new BsonDocument("$project", ListProjection)));
        var total = AggregateFacet.Create("total", PipelineDefinition<Review, AggregateCountResult>.Create(
            new BsonDocument("$count", "count")));

        // Один запит: $match (з $text, якщо є пошук) → $facet { items: сторінка, total: кількість }
        var result = await _reviews.Aggregate()
            .Match(BuildFilter(filter))
            .Facet(items, total)
            .SingleAsync(ct);

        var pageItems = result.Facets.Single(f => f.Name == "items").Output<ReviewDto>();
        var count = result.Facets.Single(f => f.Name == "total").Output<AggregateCountResult>().FirstOrDefault()?.Count ?? 0;
        return new PagedResult<ReviewDto>(pageItems, count, page, pageSize);
    }

    public async Task<MovieRatingDto?> GetMovieRatingAsync(int movieId, CancellationToken ct)
    {
        // $match (індекс ix_reviews_movie_recent) → $group за оцінкою → розподіл; середнє рахуємо з розподілу
        var groups = await _reviews.Aggregate()
            .Match(r => r.MovieId == movieId)
            .Group(new BsonDocument
            {
                { "_id", "$rating" },
                { "count", new BsonDocument("$sum", 1) },
                { "movieTitle", new BsonDocument("$last", "$movieTitle") }
            })
            .Sort(new BsonDocument("_id", 1))
            .ToListAsync(ct);

        if (groups.Count == 0) return null;

        var distribution = Enumerable.Range(1, 5).ToDictionary(r => r, _ => 0);
        foreach (var g in groups) distribution[g["_id"].ToInt32()] = g["count"].ToInt32();
        var count = distribution.Values.Sum();
        var average = Math.Round(distribution.Sum(kv => kv.Key * kv.Value) / (double)count, 2);

        return new MovieRatingDto(movieId, groups[^1]["movieTitle"].AsString, average, count, distribution);
    }

    private static FilterDefinition<Review> BuildFilter(ReviewListFilter f)
    {
        var b = Builders<Review>.Filter;
        var filter = b.Empty;

        if (f.Search is not null) filter &= b.Text(f.Search);          // text-індекс tx_reviews_text
        if (f.MovieId is { } movieId) filter &= b.Eq(r => r.MovieId, movieId);
        if (f.AuthorId is { } authorId) filter &= b.Eq(r => r.Author.CustomerId, authorId);
        if (f.Kind is { } kind) filter &= b.Eq(r => r.Kind, kind);
        if (f.MinRating is { } minRating) filter &= b.Gte("rating", minRating);
        return filter;
    }

    // Сортування підкріплене складеними індексами (movieId, createdAt) / (author.customerId, createdAt)
    private static BsonDocument SortFor(string sortBy) => sortBy switch
    {
        ReviewSort.Oldest => new BsonDocument { { "createdAt", 1 }, { "_id", 1 } },
        ReviewSort.Rating => new BsonDocument { { "rating", -1 }, { "createdAt", -1 }, { "_id", -1 } },
        ReviewSort.Likes => new BsonDocument { { "likes", -1 }, { "createdAt", -1 }, { "_id", -1 } },
        _ => new BsonDocument { { "createdAt", -1 }, { "_id", -1 } }
    };
}
