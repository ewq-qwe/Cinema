using MongoDB.Bson;
using Reviews.Domain.Entities;

namespace Reviews.Domain.Interfaces;

/// <summary>
/// Контракти доступу до даних (запис). Оголошено в Domain, реалізація на MongoDB.Driver — в Infrastructure.
/// UpdateAsync перевіряє Version (оптимістична конкурентність): чужа зміна → ConflictException.
/// </summary>
public interface IReviewRepository
{
    Task<Review?> GetByIdAsync(ObjectId id, CancellationToken ct = default);
    Task<bool> ExistsAsync(int movieId, int customerId, CancellationToken ct = default);
    Task AddAsync(Review review, CancellationToken ct = default);
    Task UpdateAsync(Review review, CancellationToken ct = default);
    Task DeleteAsync(ObjectId id, int expectedVersion, CancellationToken ct = default);
}

public interface IDiscussionRepository
{
    Task<Discussion?> GetByIdAsync(ObjectId id, CancellationToken ct = default);
    Task<bool> ExistsAsync(ObjectId id, CancellationToken ct = default);
    Task AddAsync(Discussion discussion, CancellationToken ct = default);
    Task UpdateAsync(Discussion discussion, CancellationToken ct = default);
}

public interface IMessageRepository
{
    Task<bool> ExistsInDiscussionAsync(ObjectId messageId, ObjectId discussionId, CancellationToken ct = default);
    Task AddAsync(Message message, CancellationToken ct = default);
}
