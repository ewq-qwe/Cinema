using Reviews.Domain.Common;
using Reviews.Domain.Exceptions;

namespace Reviews.Domain.ValueObjects;

/// <summary>
/// Лічильники обговорення — вкладений документ <c>stats { messagesCount, lastActivityAt }</c>.
/// Незмінний: нове повідомлення дає новий об'єкт через <see cref="WithMessage"/>.
/// </summary>
public sealed class DiscussionStats : ValueObject
{
    public static DiscussionStats Empty { get; } = new(0, null);

    public int MessagesCount { get; }
    public DateTime? LastActivityAt { get; }

    public DiscussionStats(int messagesCount, DateTime? lastActivityAt)
    {
        if (messagesCount < 0)
            throw new DomainException("Кількість повідомлень не може бути від'ємною.");
        if (messagesCount > 0 && lastActivityAt is null)
            throw new DomainException("Для обговорення з повідомленнями потрібен час останньої активності.");
        MessagesCount = messagesCount;
        LastActivityAt = lastActivityAt;
    }

    public DiscussionStats WithMessage(DateTime at) =>
        new(MessagesCount + 1, LastActivityAt is { } last && last > at ? last : at);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return MessagesCount;
        yield return LastActivityAt;
    }
}
