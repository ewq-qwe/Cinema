using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using Reviews.Application.Common.Models;
using Reviews.Domain.Entities;
using Reviews.Domain.Enums;
using Reviews.Domain.ValueObjects;
using Reviews.Infrastructure.Persistence;

namespace Reviews.Tests;

/// <summary>BsonClassMap дає структуру документа з Лаб. 01 — перевіряється без сервера MongoDB.</summary>
public class BsonMappingTests
{
    static BsonMappingTests() => MongoMappings.Register();

    private static string SamplesDir([System.Runtime.CompilerServices.CallerFilePath] string path = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "..", "..", "..", "docs", "samples"));

    [Fact]
    public void Full_review_is_stored_with_lab01_structure_without_dotnet_internals()
    {
        var review = new Review(1, "Карпатський експрес", new Author(1, "Олена К."), ReviewKind.Full, new Rating(5),
            "Найкращий фільм осені!", "Неймовірні краєвиди", photos: ["https://cdn.example/r1.jpg"]);
        review.AddComment("Тарас М.", "Згоден!");

        var doc = review.ToBsonDocument();

        Assert.Equal(review.Id, doc["_id"].AsObjectId);
        Assert.Equal("Карпатський експрес", doc["movieTitle"].AsString);
        Assert.Equal(1, doc["author"]["customerId"].AsInt32);           // вкладений Value Object
        Assert.Equal("Олена К.", doc["author"]["displayName"].AsString);
        Assert.Equal("full", doc["kind"].AsString);                     // enum → рядок у нижньому регістрі
        Assert.Equal(5, doc["rating"].AsInt32);                         // Rating VO → число
        Assert.Equal("Згоден!", doc["comments"][0]["text"].AsString);  // приватне поле _comments → comments
        Assert.Equal(0, doc["version"].AsInt32);

        string[] leaked = ["_comments", "_photos", "Comments", "Photos", "MovieTitle", "_t", "publication", "pros"];
        Assert.DoesNotContain(doc.Names, name => leaked.Contains(name));
    }

    [Fact]
    public void Quick_review_has_no_optional_fields_flexible_schema()
    {
        var doc = new Review(1, "Фільм", new Author(2, "Тарас М."), ReviewKind.Quick, new Rating(4)).ToBsonDocument();

        Assert.Equal("quick", doc["kind"].AsString);
        Assert.DoesNotContain(doc.Names, n => n is "comments" or "photos" or "title" or "text" or "hasSpoilers" or "discussionId");
    }

    [Fact]
    public void Lab01_sample_documents_deserialize_into_domain_entities()
    {
        var reviews = BsonSerializer.Deserialize<BsonArray>(File.ReadAllText(Path.Combine(SamplesDir(), "reviews.json")))
            .Select(d => BsonSerializer.Deserialize<Review>(d.AsBsonDocument)).ToList();
        var discussions = BsonSerializer.Deserialize<BsonArray>(File.ReadAllText(Path.Combine(SamplesDir(), "discussions.json")))
            .Select(d => BsonSerializer.Deserialize<Discussion>(d.AsBsonDocument)).ToList();

        var first = reviews.Single(r => r.Id == ObjectId.Parse("670000000000000000000001"));
        Assert.Equal(new Author(1, "Олена К."), first.Author);   // VO створено через конструктор
        Assert.Equal(new Rating(5), first.Rating);
        Assert.Equal(2, first.Comments.Count);
        Assert.Equal(DateTimeKind.Utc, first.CreatedAt.Kind);

        var critic = reviews.Single(r => r.Kind == ReviewKind.Critic);
        Assert.Equal(["акторська гра", "саундтрек"], critic.Pros);

        var pinned = discussions.Single(d => d.IsPinned);
        Assert.Equal(3, pinned.Stats.MessagesCount);
        Assert.Single(pinned.LastMessages);
    }

    [Fact]
    public void Round_trip_keeps_state_and_version()
    {
        var discussion = new Discussion(3, "Мандрівка до зірок", "Для дітей?", new Author(3, "Ірина Б."), ["діти"]);
        discussion.RegisterMessage(new Message(discussion.Id, new Author(1, "Олена К."), "Так, 6+"));
        discussion.BumpVersion();

        var copy = BsonSerializer.Deserialize<Discussion>(discussion.ToBsonDocument());

        Assert.Equal(discussion.Id, copy.Id);
        Assert.Equal(1, copy.Version);
        // BSON Date зберігає мілісекунди, тож порівнюємо з точністю до 1 мс
        Assert.Equal(discussion.Stats.MessagesCount, copy.Stats.MessagesCount);
        Assert.Equal(discussion.Stats.LastActivityAt!.Value, copy.Stats.LastActivityAt!.Value, TimeSpan.FromMilliseconds(1));
        Assert.Equal(discussion.LastMessages.Single().MessageId, copy.LastMessages.Single().MessageId);
        Assert.Equal(["діти"], copy.Tags);
    }

    [Fact]
    public void New_discussion_without_activity_round_trips()
    {
        // Регресія: lastActivityAt = null не записується, а creator DiscussionStats має відновитися без нього
        var discussion = new Discussion(1, "Фільм", "Нова тема", new Author(1, "Олена К."));
        var doc = discussion.ToBsonDocument();

        Assert.False(doc["stats"].AsBsonDocument.Contains("lastActivityAt"));
        var copy = BsonSerializer.Deserialize<Discussion>(doc);
        Assert.Equal(DiscussionStats.Empty, copy.Stats);
    }

    [Fact]
    public void Projected_document_maps_ObjectId_to_string_in_dto()
    {
        var id = ObjectId.GenerateNewId();
        var projected = new BsonDocument
        {
            { "_id", id }, { "movieId", 1 }, { "authorName", "Олена К." }, { "rating", 5 },
            { "kind", "full" }, { "commentsCount", 2 }, { "discussionId", ObjectId.Parse("670000000000000000000d01") }
        };

        var dto = BsonSerializer.Deserialize<ReviewDetailsDto>(projected);

        Assert.Equal(id.ToString(), dto.Id);
        Assert.Equal("670000000000000000000d01", dto.DiscussionId);
        Assert.Equal(2, dto.CommentsCount);
    }
}
