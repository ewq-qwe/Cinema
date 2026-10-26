using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Reviews.Domain.Entities;
using Reviews.Infrastructure.Persistence;

namespace Reviews.Infrastructure.Seeding;

/// <summary>
/// Індекси для кожної колекції визначено в коді й створюються під час старту.
/// Імена збігаються з db/p3/collections.js (Лаб. 01): повторне створення наявного індексу MongoDB не дублює.
/// </summary>
public sealed class IndexCreationService(
    MongoDbContext context,
    IOptions<MongoDbOptions> options,
    ILogger<IndexCreationService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        if (!options.Value.CreateIndexesOnStartup) return;

        try
        {
            var r = Builders<Review>.IndexKeys;
            await context.Reviews.Indexes.CreateManyAsync(
            [
                // сторінка фільму: відгуки, нові спочатку
                new CreateIndexModel<Review>(r.Ascending(x => x.MovieId).Descending(x => x.CreatedAt),
                    new CreateIndexOptions { Name = "ix_reviews_movie_recent" }),
                // «мої відгуки» глядача
                new CreateIndexModel<Review>(r.Ascending("author.customerId").Descending(x => x.CreatedAt),
                    new CreateIndexOptions { Name = "ix_reviews_author" }),
                // цілісність: один глядач — один відгук на фільм (дублікат → DuplicateKey → 409)
                new CreateIndexModel<Review>(r.Ascending(x => x.MovieId).Ascending("author.customerId"),
                    new CreateIndexOptions { Name = "ux_reviews_movie_author", Unique = true }),
                // повнотекстовий пошук; мова "none" — без англійського стемінгу для українських текстів
                new CreateIndexModel<Review>(r.Combine(r.Text(x => x.Title), r.Text(x => x.Text)),
                    new CreateIndexOptions { Name = "tx_reviews_text", DefaultLanguage = "none" })
            ], ct);

            var d = Builders<Discussion>.IndexKeys;
            await context.Discussions.Indexes.CreateOneAsync(
                new CreateIndexModel<Discussion>(
                    d.Ascending(x => x.MovieId).Descending(x => x.IsPinned).Descending("stats.lastActivityAt"),
                    new CreateIndexOptions { Name = "ix_discussions_movie" }), cancellationToken: ct);

            var m = Builders<Message>.IndexKeys;
            await context.Messages.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<Message>(m.Ascending(x => x.DiscussionId).Ascending(x => x.CreatedAt),
                    new CreateIndexOptions { Name = "ix_messages_discussion" }),
                // курсорна пагінація повідомлень теми (_id > курсор)
                new CreateIndexModel<Message>(m.Ascending(x => x.DiscussionId).Ascending(x => x.Id),
                    new CreateIndexOptions { Name = "ix_messages_discussion_cursor" })
            ], ct);

            logger.LogInformation("MongoDB: індекси колекцій reviews, discussions, messages на місці");
        }
        catch (Exception ex) when (ex is MongoException or TimeoutException)
        {
            // Сервіс стартує й без БД: запити отримають 503, а не падіння процесу
            logger.LogError(ex, "MongoDB недоступна — індекси не створено");
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
