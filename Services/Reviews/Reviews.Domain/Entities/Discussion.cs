using Reviews.Domain.Common;
using Reviews.Domain.Exceptions;
using Reviews.Domain.ValueObjects;

namespace Reviews.Domain.Entities;

/// <summary>
/// Тема обговорення фільму — документ колекції <c>discussions</c>.
/// Гібрид з Лаб. 01: останні 3 повідомлення вбудовано в <c>lastMessages[]</c>, усі — в колекції messages.
/// </summary>
public class Discussion : BaseEntity
{
    public const int TitleMinLength = 3;
    public const int TitleMaxLength = 150;
    public const int MaxTags = 10;
    public const int TagMaxLength = 30;
    public const int LastMessagesLimit = 3;

    private List<string> _tags = [];
    private List<MessagePreview> _lastMessages = [];

    public int MovieId { get; private set; }
    public string MovieTitle { get; private set; } = "";
    public string Title { get; private set; } = "";
    public Author Author { get; private set; } = null!;
    public bool IsPinned { get; private set; }
    public DiscussionStats Stats { get; private set; } = DiscussionStats.Empty;

    public IReadOnlyCollection<string> Tags => _tags.AsReadOnly();
    public IReadOnlyCollection<MessagePreview> LastMessages => _lastMessages.AsReadOnly();

    private Discussion() { } // для десеріалізації

    public Discussion(int movieId, string movieTitle, string title, Author author, IEnumerable<string>? tags = null)
    {
        if (movieId <= 0) throw new DomainException("Некоректний фільм.");
        movieTitle = movieTitle?.Trim() ?? "";
        if (movieTitle.Length == 0) throw new DomainException("Назва фільму обов'язкова.");
        title = title?.Trim() ?? "";
        if (title.Length is < TitleMinLength or > TitleMaxLength)
            throw new DomainException($"Тема має містити від {TitleMinLength} до {TitleMaxLength} символів.");

        var tagList = tags?.Select(t => t?.Trim().ToLowerInvariant() ?? "").Where(t => t.Length > 0).Distinct().ToList() ?? [];
        if (tagList.Count > MaxTags || tagList.Any(t => t.Length > TagMaxLength))
            throw new DomainException($"До {MaxTags} тегів, кожен до {TagMaxLength} символів.");

        MovieId = movieId;
        MovieTitle = movieTitle;
        Title = title;
        Author = author ?? throw new DomainException("Тема потребує автора.");
        _tags = tagList;
    }

    /// <summary>Нове повідомлення: оновлює лічильники й вбудовані останні повідомлення (не більше трьох).</summary>
    public void RegisterMessage(Message message)
    {
        if (message.DiscussionId != Id)
            throw new DomainException("Повідомлення належить іншому обговоренню.");
        if (_lastMessages.Any(m => m.MessageId == message.Id))
            return; // уже враховано (повторна спроба після конфлікту версій)

        Stats = Stats.WithMessage(message.CreatedAt);
        _lastMessages.Add(new MessagePreview(message.Id, message.Author.DisplayName, message.Text, message.CreatedAt));
        _lastMessages = _lastMessages.OrderBy(m => m.CreatedAt).TakeLast(LastMessagesLimit).ToList();
        Touch();
    }

    public void Pin()
    {
        if (IsPinned) throw new DomainException("Тему вже закріплено.");
        IsPinned = true;
        Touch();
    }

    public void Unpin()
    {
        if (!IsPinned) throw new DomainException("Тема не закріплена.");
        IsPinned = false;
        Touch();
    }
}
