using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Reviews.Domain.Entities;
using Reviews.Infrastructure.Persistence;

namespace Reviews.Infrastructure.Seeding;

/// <summary>Наповнення однієї колекції. Ідемпотентне: непорожню колекцію не чіпає.</summary>
public interface IDataSeeder
{
    Task SeedAsync(CancellationToken ct = default);
}

/// <summary>
/// Базовий сідер: дані — ті самі приклади документів з Лаб. 01 (docs/samples/*.json, Extended JSON),
/// вбудовані у збірку як ресурси. Документи читаються через BsonClassMap у доменні сутності
/// й вставляються bulk-операцією InsertManyAsync.
/// </summary>
public abstract class JsonDataSeeder<T>(IMongoCollection<T> collection, string resourceName, ILogger logger) : IDataSeeder
{
    public async Task SeedAsync(CancellationToken ct = default)
    {
        var name = collection.CollectionNamespace.CollectionName;
        if (await collection.EstimatedDocumentCountAsync(cancellationToken: ct) > 0)
        {
            logger.LogInformation("Сідінг {Collection}: колекція вже містить дані — пропущено", name);
            return;
        }

        var documents = LoadDocuments();
        await collection.InsertManyAsync(documents, cancellationToken: ct);
        logger.LogInformation("Сідінг {Collection}: додано {Count} документів", name, documents.Count);
    }

    private List<T> LoadDocuments()
    {
        var assembly = typeof(JsonDataSeeder<T>).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
                           ?? throw new InvalidOperationException($"Seed resource '{resourceName}' not found");
        using var reader = new StreamReader(stream);
        var array = BsonSerializer.Deserialize<BsonArray>(reader.ReadToEnd());
        return array.Select(doc => BsonSerializer.Deserialize<T>(doc.AsBsonDocument)).ToList();
    }
}

public sealed class ReviewSeeder(MongoDbContext context, ILogger<ReviewSeeder> logger)
    : JsonDataSeeder<Review>(context.Reviews, "Seed.reviews.json", logger);

public sealed class DiscussionSeeder(MongoDbContext context, ILogger<DiscussionSeeder> logger)
    : JsonDataSeeder<Discussion>(context.Discussions, "Seed.discussions.json", logger);

public sealed class MessageSeeder(MongoDbContext context, ILogger<MessageSeeder> logger)
    : JsonDataSeeder<Message>(context.Messages, "Seed.messages.json", logger);

/// <summary>Запускає всі IDataSeeder під час старту (після IndexCreationService). У Production вимкнено конфігурацією.</summary>
public sealed class DataSeedingService(
    IEnumerable<IDataSeeder> seeders,
    IOptions<MongoDbOptions> options,
    ILogger<DataSeedingService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        if (!options.Value.SeedOnStartup) return;
        try
        {
            foreach (var seeder in seeders)
                await seeder.SeedAsync(ct);
        }
        catch (Exception ex) when (ex is MongoException or TimeoutException)
        {
            logger.LogError(ex, "MongoDB недоступна — сідінг пропущено");
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
