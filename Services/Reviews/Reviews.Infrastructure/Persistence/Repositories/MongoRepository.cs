using MongoDB.Bson;
using MongoDB.Driver;
using Reviews.Domain.Common;
using Reviews.Domain.Exceptions;

namespace Reviews.Infrastructure.Persistence.Repositories;

/// <summary>
/// Спільна частина репозиторіїв на MongoDB.Driver: пошук за _id, вставка й оновлення з оптимістичною конкурентністю.
/// </summary>
public abstract class MongoRepository<T>(IMongoCollection<T> collection) where T : BaseEntity
{
    protected IMongoCollection<T> Collection { get; } = collection;

    protected static FilterDefinitionBuilder<T> Filter => Builders<T>.Filter;

    public async Task<T?> GetByIdAsync(ObjectId id, CancellationToken ct = default) =>
        await Collection.Find(ById(id)).FirstOrDefaultAsync(ct);

    public Task AddAsync(T entity, CancellationToken ct = default) =>
        Collection.InsertOneAsync(entity, cancellationToken: ct);

    /// <summary>
    /// Документ замінюється, лише якщо в БД досі очікувана версія; у БД записується версія + 1.
    /// MatchedCount = 0 → хтось змінив (або видалив) документ раніше → ConflictException (409).
    /// </summary>
    public async Task UpdateAsync(T entity, CancellationToken ct = default)
    {
        var expectedVersion = entity.Version;
        entity.BumpVersion();

        var result = await Collection.ReplaceOneAsync(ById(entity.Id) & WithVersion(expectedVersion), entity, cancellationToken: ct);
        if (result.MatchedCount == 0)
            throw new ConflictException(
                $"{typeof(T).Name} {entity.Id} уже змінено іншим запитом (очікувалась версія {expectedVersion}). Оновіть дані й повторіть.");
    }

    protected static FilterDefinition<T> ById(ObjectId id) => Filter.Eq(e => e.Id, id);

    /// <summary>Документи, створені скриптами Лаб. 01, не мають поля version — для них це версія 0.</summary>
    protected static FilterDefinition<T> WithVersion(int version) =>
        version == 0
            ? Filter.Or(Filter.Eq(e => e.Version, 0), Filter.Exists(e => e.Version, false))
            : Filter.Eq(e => e.Version, version);
}
