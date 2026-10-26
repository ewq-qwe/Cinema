using MongoDB.Bson;
using MongoDB.Driver;
using Reviews.Domain.Entities;
using Reviews.Domain.Exceptions;
using Reviews.Domain.Interfaces;

namespace Reviews.Infrastructure.Persistence.Repositories;

public sealed class ReviewRepository(MongoDbContext context)
    : MongoRepository<Review>(context.Reviews), IReviewRepository
{
    // Запит покривається унікальним індексом ux_reviews_movie_author
    public Task<bool> ExistsAsync(int movieId, int customerId, CancellationToken ct = default) =>
        Collection.Find(r => r.MovieId == movieId && r.Author.CustomerId == customerId).AnyAsync(ct);

    public async Task DeleteAsync(ObjectId id, int expectedVersion, CancellationToken ct = default)
    {
        var result = await Collection.DeleteOneAsync(ById(id) & WithVersion(expectedVersion), ct);
        if (result.DeletedCount == 0)
            throw new ConflictException($"Відгук {id} змінено або вже видалено (очікувалась версія {expectedVersion}).");
    }
}

public sealed class DiscussionRepository(MongoDbContext context)
    : MongoRepository<Discussion>(context.Discussions), IDiscussionRepository
{
    public Task<bool> ExistsAsync(ObjectId id, CancellationToken ct = default) =>
        Collection.Find(ById(id)).AnyAsync(ct);
}

public sealed class MessageRepository(MongoDbContext context)
    : MongoRepository<Message>(context.Messages), IMessageRepository
{
    public Task<bool> ExistsInDiscussionAsync(ObjectId messageId, ObjectId discussionId, CancellationToken ct = default) =>
        Collection.Find(m => m.Id == messageId && m.DiscussionId == discussionId).AnyAsync(ct);
}
