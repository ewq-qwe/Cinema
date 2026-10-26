using MongoDB.Bson;
using Reviews.Domain.Common;
using Reviews.Domain.Enums;
using Reviews.Domain.Exceptions;
using Reviews.Domain.ValueObjects;

namespace Reviews.Domain.Entities;

/// <summary>
/// Відгук на фільм — корінь агрегату, документ колекції <c>reviews</c> з Лаб. 01.
/// Гнучка схема: набір полів залежить від виду (<see cref="ReviewKind"/>), і правила кожного виду перевіряє сама сутність.
/// Стан змінюється лише методами; setter-и приватні.
/// </summary>
public class Review : BaseEntity
{
    public const int TitleMaxLength = 120;
    public const int TextMaxLength = 3000;
    public const int PublicationMaxLength = 100;
    public const int MaxPhotos = 5;
    public const int MaxComments = 20;
    public const int MaxPoints = 10;
    public const int PointMaxLength = 100;

    // Вкладені масиви. Поля не readonly: драйвер MongoDB записує їх під час десеріалізації (мапінг — в Infrastructure).
    private List<Comment> _comments = [];
    private List<string> _photos = [];
    private List<string> _pros = [];
    private List<string> _cons = [];

    public int MovieId { get; private set; }                   // посилання на Каталог (Movies.Id)
    public string MovieTitle { get; private set; } = "";       // денормалізовано з Каталогу для читання
    public Author Author { get; private set; } = null!;        // Value Object → вкладений документ
    public ReviewKind Kind { get; private set; }
    public Rating Rating { get; private set; } = null!;        // Value Object → число
    public string? Title { get; private set; }
    public string? Text { get; private set; }
    public bool? HasSpoilers { get; private set; }
    public string? Publication { get; private set; }
    public ObjectId? DiscussionId { get; private set; }        // reference на discussions
    public int Likes { get; private set; }

    public IReadOnlyCollection<Comment> Comments => _comments.AsReadOnly();
    public IReadOnlyCollection<string> Photos => _photos.AsReadOnly();
    public IReadOnlyCollection<string> Pros => _pros.AsReadOnly();
    public IReadOnlyCollection<string> Cons => _cons.AsReadOnly();

    private Review() { } // для десеріалізації

    public Review(int movieId, string movieTitle, Author author, ReviewKind kind, Rating rating,
        string? title = null, string? text = null, bool hasSpoilers = false,
        IEnumerable<string>? photos = null, string? publication = null,
        IEnumerable<string>? pros = null, IEnumerable<string>? cons = null)
    {
        if (movieId <= 0) throw new DomainException("Некоректний фільм.");
        if (!Enum.IsDefined(kind)) throw new DomainException("Невідомий вид відгуку.");

        MovieId = movieId;
        RenameMovie(movieTitle);
        Author = author ?? throw new DomainException("Відгук потребує автора.");
        Kind = kind;
        ApplyContent(rating, title, text, hasSpoilers, photos, publication, pros, cons);
        UpdatedAt = null;
    }

    /// <summary>Редагування відгуку. Вид відгуку не змінюється — від нього залежить структура документа.</summary>
    public void Edit(Rating rating, string? title, string? text, bool hasSpoilers,
        IEnumerable<string>? photos, string? publication, IEnumerable<string>? pros, IEnumerable<string>? cons)
    {
        ApplyContent(rating, title, text, hasSpoilers, photos, publication, pros, cons);
    }

    public Comment AddComment(string author, string text)
    {
        if (Kind != ReviewKind.Full)
            throw new DomainException("Коментувати можна лише повні відгуки.");
        if (_comments.Count >= MaxComments)
            throw new DomainException($"Під відгуком уже {MaxComments} коментарів — продовжіть в обговоренні фільму.");

        var comment = new Comment(author, text, DateTime.UtcNow);
        _comments.Add(comment);
        Touch();
        return comment;
    }

    public void Like()
    {
        Likes++;
        Touch();
    }

    public void LinkDiscussion(ObjectId discussionId)
    {
        if (Kind != ReviewKind.Full)
            throw new DomainException("Обговорення прив'язується лише до повного відгуку.");
        DiscussionId = discussionId;
        Touch();
    }

    /// <summary>Оновлення денормалізованої назви (подія «фільм перейменовано» з Каталогу).</summary>
    public void RenameMovie(string movieTitle)
    {
        movieTitle = movieTitle?.Trim() ?? "";
        if (movieTitle.Length is 0 or > 200)
            throw new DomainException("Назва фільму обов'язкова і не довша за 200 символів.");
        MovieTitle = movieTitle;
        Touch();
    }

    private void ApplyContent(Rating rating, string? title, string? text, bool hasSpoilers,
        IEnumerable<string>? photos, string? publication, IEnumerable<string>? pros, IEnumerable<string>? cons)
    {
        title = Normalize(title);
        text = Normalize(text);
        publication = Normalize(publication);
        var photoList = Clean(photos);
        var prosList = Clean(pros);
        var consList = Clean(cons);

        switch (Kind)
        {
            case ReviewKind.Quick:
                if (title is not null || text is not null || publication is not null
                    || photoList.Count > 0 || prosList.Count > 0 || consList.Count > 0)
                    throw new DomainException("Швидкий відгук містить лише оцінку.");
                break;

            case ReviewKind.Full:
                RequireText(text);
                if (title?.Length > TitleMaxLength)
                    throw new DomainException($"Заголовок не довший за {TitleMaxLength} символів.");
                if (photoList.Count > MaxPhotos)
                    throw new DomainException($"Не більше {MaxPhotos} фото.");
                if (photoList.Any(p => !Uri.TryCreate(p, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")))
                    throw new DomainException("Фото мають бути абсолютними http(s)-посиланнями.");
                if (publication is not null || prosList.Count > 0 || consList.Count > 0)
                    throw new DomainException("Видання, плюси й мінуси є лише в рецензії критика.");
                break;

            case ReviewKind.Critic:
                RequireText(text);
                if (publication is null || publication.Length > PublicationMaxLength)
                    throw new DomainException($"Для рецензії критика вкажіть видання (до {PublicationMaxLength} символів).");
                if (prosList.Count > MaxPoints || consList.Count > MaxPoints
                    || prosList.Concat(consList).Any(p => p.Length > PointMaxLength))
                    throw new DomainException($"Плюсів і мінусів — до {MaxPoints}, кожен до {PointMaxLength} символів.");
                if (title is not null || photoList.Count > 0)
                    throw new DomainException("У рецензії критика немає заголовка і фото.");
                break;
        }

        Rating = rating ?? throw new DomainException("Відгук потребує оцінки.");
        Title = title;
        Text = text;
        HasSpoilers = Kind == ReviewKind.Quick ? null : hasSpoilers;
        Publication = publication;
        _photos = photoList;
        _pros = prosList;
        _cons = consList;
        Touch();
    }

    private static void RequireText(string? text)
    {
        if (text is null || text.Length > TextMaxLength)
            throw new DomainException($"Текст відгуку обов'язковий і не довший за {TextMaxLength} символів.");
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static List<string> Clean(IEnumerable<string>? values) =>
        values?.Select(v => v?.Trim() ?? "").Where(v => v.Length > 0).Distinct().ToList() ?? [];
}
