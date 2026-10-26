using MongoDB.Bson;
using MongoDB.Driver;
using Reviews.Application.Common.Interfaces;
using Reviews.Application.Common.Models;
using Reviews.Domain.Entities;

namespace Reviews.Infrastructure.Persistence.Queries;

public sealed class DiscussionQueries(MongoDbContext context) : IDiscussionQueries
{
    private static readonly BsonDocument DiscussionProjection = new()
    {
        { "movieId", 1 }, { "movieTitle", 1 }, { "title", 1 },
        { "authorId", "$author.customerId" }, { "authorName", "$author.displayName" },
        { "tags", 1 }, { "isPinned", 1 },
        { "messagesCount", "$stats.messagesCount" }, { "lastActivityAt", "$stats.lastActivityAt" },
        { "lastMessages", 1 }, { "createdAt", 1 }, { "version", 1 }
    };

    private static readonly BsonDocument MessageProjection = new()
    {
        { "discussionId", 1 },
        { "authorId", "$author.customerId" }, { "authorName", "$author.displayName" },
        { "text", 1 }, { "replyTo", 1 }, { "createdAt", 1 }
    };

    private readonly IMongoCollection<Discussion> _discussions = context.Discussions;
    private readonly IMongoCollection<Message> _messages = context.Messages;

    public async Task<DiscussionDto?> GetByIdAsync(ObjectId id, CancellationToken ct) =>
        await _discussions.Find(d => d.Id == id).Project<DiscussionDto>(DiscussionProjection).FirstOrDefaultAsync(ct);

    public async Task<PagedResult<DiscussionDto>> GetListAsync(int? movieId, int page, int pageSize, CancellationToken ct)
    {
        var filter = movieId is { } id ? Builders<Discussion>.Filter.Eq(d => d.MovieId, id) : Builders<Discussion>.Filter.Empty;

        // Стрічка тем: закріплені, потім найактивніші (індекс ix_discussions_movie)
        var items = await _discussions.Find(filter)
            .Sort(new BsonDocument { { "isPinned", -1 }, { "stats.lastActivityAt", -1 }, { "_id", -1 } })
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .Project<DiscussionDto>(DiscussionProjection)
            .ToListAsync(ct);
        var total = await _discussions.CountDocumentsAsync(filter, cancellationToken: ct);

        return new PagedResult<DiscussionDto>(items, total, page, pageSize);
    }

    public async Task<CursorPage<MessageDto>> GetMessagesAsync(ObjectId discussionId, ObjectId? after, int limit, CancellationToken ct)
    {
        // Курсор: _id > останнього отриманого. ObjectId зростає разом із часом створення,
        // тому хронологічний порядок = порядок _id; індекс ix_messages_discussion_cursor (discussionId, _id)
        var b = Builders<Message>.Filter;
        var filter = b.Eq(m => m.DiscussionId, discussionId);
        if (after is { } cursor) filter &= b.Gt(m => m.Id, cursor);

        var items = await _messages.Find(filter)
            .SortBy(m => m.Id)
            .Limit(limit + 1)                      // +1: чи є наступна сторінка
            .Project<MessageDto>(MessageProjection)
            .ToListAsync(ct);

        var hasMore = items.Count > limit;
        if (hasMore) items.RemoveAt(items.Count - 1);
        return new CursorPage<MessageDto>(items, hasMore ? items[^1].Id : null, hasMore);
    }
}
