using MongoDB.Bson;
using Reviews.Domain.Common;
using Reviews.Domain.Exceptions;
using Reviews.Domain.ValueObjects;

namespace Reviews.Domain.Entities;

/// <summary>
/// Повідомлення обговорення — окрема колекція <c>messages</c> (reference <c>discussionId</c>):
/// повідомлень може бути тисячі, тому вони не вбудовуються в документ теми.
/// </summary>
public class Message : BaseEntity
{
    public const int TextMaxLength = 2000;

    public ObjectId DiscussionId { get; private set; }
    public Author Author { get; private set; } = null!;
    public string Text { get; private set; } = "";
    public ObjectId? ReplyTo { get; private set; }

    private Message() { } // для десеріалізації

    public Message(ObjectId discussionId, Author author, string text, ObjectId? replyTo = null)
    {
        if (discussionId == ObjectId.Empty) throw new DomainException("Повідомлення має належати обговоренню.");
        text = text?.Trim() ?? "";
        if (text.Length is 0 or > TextMaxLength)
            throw new DomainException($"Текст повідомлення обов'язковий і не довший за {TextMaxLength} символів.");
        if (replyTo == ObjectId.Empty) throw new DomainException("Некоректне посилання на повідомлення.");

        DiscussionId = discussionId;
        Author = author ?? throw new DomainException("Повідомлення потребує автора.");
        Text = text;
        ReplyTo = replyTo;
    }
}
