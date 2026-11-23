using MongoDB.Bson;
using MongoDB.Driver;
using Reviews.Application.Common.Interfaces;

namespace Reviews.Infrastructure.Persistence.Repositories;

/// <summary>
/// Лаб. 08: синхронізація movieTitle з Каталогом у MongoDB.
/// <list type="bullet">
/// <item><c>processedEvents</c> — { _id: EventId, eventType, processedAt }: унікальний _id = ключ ідемпотентності; TTL 30 днів;</item>
/// <item><c>catalogMovies</c> — { _id: movieId, title, titleChangedAt }: остання застосована назва і час події (захист від out-of-order).</item>
/// </list>
/// Документи відгуків оновлюються через BsonDocument за іменами полів BSON (movieId, movieTitle — camelCase з MongoMappings).
/// </summary>
public sealed class CatalogSyncRepository(MongoDbContext context) : ICatalogSyncRepository
{
    public const string ProcessedEventsCollection = "processedEvents";
    public const string CatalogMoviesCollection = "catalogMovies";

    private IMongoCollection<BsonDocument> ProcessedEvents => context.Database.GetCollection<BsonDocument>(ProcessedEventsCollection);
    private IMongoCollection<BsonDocument> CatalogMovies => context.Database.GetCollection<BsonDocument>(CatalogMoviesCollection);
    private IMongoCollection<BsonDocument> Reviews => context.Database.GetCollection<BsonDocument>(MongoDbContext.ReviewsCollection);
    private IMongoCollection<BsonDocument> Discussions => context.Database.GetCollection<BsonDocument>(MongoDbContext.DiscussionsCollection);

    public async Task<bool> IsProcessedAsync(Guid eventId, CancellationToken ct) =>
        await ProcessedEvents.Find(new BsonDocument("_id", eventId.ToString())).Limit(1).AnyAsync(ct);

    public async Task MarkProcessedAsync(Guid eventId, string eventType, CancellationToken ct)
    {
        try
        {
            await ProcessedEvents.InsertOneAsync(new BsonDocument
            {
                ["_id"] = eventId.ToString(),
                ["eventType"] = eventType,
                ["processedAt"] = DateTime.UtcNow
            }, cancellationToken: ct);
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            // Уже записано паралельною / попередньою обробкою — стан той самий
        }
    }

    public async Task<bool> TryAdvanceTitleAsync(int movieId, string title, DateTime occurredAt, CancellationToken ct)
    {
        // Умовний upsert: фільтр «той самий фільм І збережена зміна не новіша за цю подію».
        // Якщо документ є, але новіший — фільтр нічого не знаходить, upsert пробує вставити _id, що вже існує → DuplicateKey = подія застаріла.
        var filter = new BsonDocument
        {
            ["_id"] = movieId,
            ["$or"] = new BsonArray
            {
                new BsonDocument("titleChangedAt", new BsonDocument("$lte", occurredAt)),
                new BsonDocument("titleChangedAt", new BsonDocument("$exists", false))
            }
        };
        var update = new BsonDocument("$set", new BsonDocument { ["title"] = title, ["titleChangedAt"] = occurredAt, ["syncedAt"] = DateTime.UtcNow });
        try
        {
            await CatalogMovies.UpdateOneAsync(filter, update, new UpdateOptions { IsUpsert = true }, ct);
            return true;
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<string>> GetReviewIdsAsync(int movieId, CancellationToken ct)
    {
        var ids = await Reviews.Find(new BsonDocument("movieId", movieId))
            .Project(new BsonDocument("_id", 1))
            .ToListAsync(ct);
        return ids.Select(d => d["_id"].ToString()!).ToList();
    }

    public async Task<(long Reviews, long Discussions)> RenameMovieAsync(int movieId, string title, CancellationToken ct)
    {
        var filter = new BsonDocument("movieId", movieId);
        var set = new BsonDocument("$set", new BsonDocument("movieTitle", title));
        // Версія документа (ETag) свідомо не змінюється: назва — денормалізоване поле для читання, а не зміст відгуку
        var reviews = await Reviews.UpdateManyAsync(filter, set, cancellationToken: ct);
        var discussions = await Discussions.UpdateManyAsync(filter, set, cancellationToken: ct);
        return (reviews.ModifiedCount, discussions.ModifiedCount);
    }
}
