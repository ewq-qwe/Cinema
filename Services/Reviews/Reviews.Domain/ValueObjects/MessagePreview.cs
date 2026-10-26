using MongoDB.Bson;
using Reviews.Domain.Common;
using Reviews.Domain.Exceptions;

namespace Reviews.Domain.ValueObjects;

/// <summary>
/// Короткий зміст повідомлення, вбудований у обговорення (<c>lastMessages[]</c>, гібридне моделювання з Лаб. 01):
/// стрічку тем можна показати без читання колекції messages.
/// </summary>
public sealed class MessagePreview : ValueObject
{
    public const int ExcerptMaxLength = 80;

    public ObjectId MessageId { get; }
    public string Author { get; }
    public string Excerpt { get; }
    public DateTime CreatedAt { get; }

    public MessagePreview(ObjectId messageId, string author, string excerpt, DateTime createdAt)
    {
        if (messageId == ObjectId.Empty)
            throw new DomainException("Прев'ю має посилатися на повідомлення.");
        if (string.IsNullOrWhiteSpace(author) || string.IsNullOrWhiteSpace(excerpt))
            throw new DomainException("Прев'ю повідомлення потребує автора й тексту.");

        MessageId = messageId;
        Author = author.Trim();
        Excerpt = Shorten(excerpt.Trim());
        CreatedAt = createdAt;
    }

    private static string Shorten(string text) =>
        text.Length <= ExcerptMaxLength ? text : text[..(ExcerptMaxLength - 1)].TrimEnd() + "…";

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return MessageId;
        yield return Author;
        yield return Excerpt;
        yield return CreatedAt;
    }
}
