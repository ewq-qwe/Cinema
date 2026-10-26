using Catalog.Domain.Exceptions;

namespace Catalog.Domain.Entities;

/// <summary>
/// Фільм — корінь агрегату каталогу (аналог CatalogItem з eShop).
/// Стан змінюється лише методами з перевіркою правил; setter-и приватні, конструктор валідує дані.
/// </summary>
public class Movie
{
    public const int TitleMaxLength = 200;
    public const int MinReleaseYear = 1895;
    public const int MaxDurationMinutes = 600;
    public static readonly string[] AgeRatings = ["0+", "6+", "12+", "16+", "18+"];

    private readonly List<MovieCredit> _credits = [];

    public int Id { get; private set; }
    public string Title { get; private set; } = "";
    public int ReleaseYear { get; private set; }
    public int DurationMinutes { get; private set; }
    public string AgeRating { get; private set; } = "0+";
    public bool IsArchived { get; private set; }

    public int GenreId { get; private set; }
    public Genre Genre { get; private set; } = null!;

    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = [];          // оптимістична конкурентність

    public MovieDetail? Detail { get; private set; }               // 1:1
    public IReadOnlyCollection<MovieCredit> Credits => _credits;   // M:N через MovieCredit

    private Movie() { } // для EF Core

    public Movie(string title, int releaseYear, int durationMinutes, string ageRating, int genreId)
    {
        SetTitleAndYear(title, releaseYear);
        ChangeDuration(durationMinutes);
        ChangeAgeRating(ageRating);
        MoveToGenre(genreId);
        CreatedAt = DateTime.UtcNow;
    }

    public void SetTitleAndYear(string title, int releaseYear)
    {
        EnsureNotArchived();
        title = title?.Trim() ?? "";
        if (title.Length is 0 or > TitleMaxLength)
            throw new DomainException($"Назва фільму обов'язкова і не довша за {TitleMaxLength} символів.");
        if (releaseYear < MinReleaseYear || releaseYear > DateTime.UtcNow.Year + 5)
            throw new DomainException($"Рік випуску має бути між {MinReleaseYear} і {DateTime.UtcNow.Year + 5}.");
        Title = title;
        ReleaseYear = releaseYear;
        Touch();
    }

    public void ChangeDuration(int minutes)
    {
        EnsureNotArchived();
        if (minutes is < 1 or > MaxDurationMinutes)
            throw new DomainException($"Тривалість має бути від 1 до {MaxDurationMinutes} хвилин.");
        DurationMinutes = minutes;
        Touch();
    }

    public void ChangeAgeRating(string ageRating)
    {
        EnsureNotArchived();
        if (!AgeRatings.Contains(ageRating))
            throw new DomainException($"Віковий рейтинг має бути одним із: {string.Join(", ", AgeRatings)}.");
        AgeRating = ageRating;
        Touch();
    }

    public void MoveToGenre(int genreId)
    {
        EnsureNotArchived();
        if (genreId <= 0) throw new DomainException("Некоректний жанр.");
        GenreId = genreId;
        Touch();
    }

    /// <summary>Створює або оновлює опис (1:1).</summary>
    public void SetDetail(string description, string country, string? trailerUrl, string? posterUrl, decimal? budget = null)
    {
        EnsureNotArchived();
        if (Detail is null) Detail = new MovieDetail(description, country, trailerUrl, posterUrl, budget);
        else Detail.Update(description, country, trailerUrl, posterUrl, budget);
        Touch();
    }

    /// <summary>
    /// Додає людину в титри. Правила: одна людина — одна роль кожного типу;
    /// у режисера немає персонажа, в актора він обов'язковий; порядок у титрах актора унікальний.
    /// </summary>
    public MovieCredit AddCredit(Person person, CreditRole role, string? characterName, int billingOrder)
    {
        EnsureNotArchived();
        // Порівнюємо за посиланням або за Id: у ще не збережених людей Id = 0, і порівняння лише за Id дало б хибний дубль
        if (_credits.Any(c => c.Role == role && (ReferenceEquals(c.Person, person) || (person.Id != 0 && c.PersonId == person.Id))))
            throw new DomainException($"{person.FullName} уже є в титрах фільму в ролі {role}.");
        if (billingOrder < 0)
            throw new DomainException("Порядок у титрах не може бути від'ємним.");

        characterName = string.IsNullOrWhiteSpace(characterName) ? null : characterName.Trim();
        if (role == CreditRole.Director && characterName is not null)
            throw new DomainException("У режисера не може бути персонажа.");
        if (role == CreditRole.Actor)
        {
            if (characterName is null)
                throw new DomainException("Для актора вкажіть ім'я персонажа.");
            if (characterName.Length > MovieCredit.CharacterNameMaxLength)
                throw new DomainException($"Ім'я персонажа не довше за {MovieCredit.CharacterNameMaxLength} символів.");
            if (_credits.Any(c => c.Role == CreditRole.Actor && c.BillingOrder == billingOrder))
                throw new DomainException($"Позиція {billingOrder} у титрах уже зайнята.");
        }

        var credit = new MovieCredit(this, person, role, characterName, billingOrder);
        _credits.Add(credit);
        Touch();
        return credit;
    }

    public void RemoveCredit(int personId, CreditRole role)
    {
        EnsureNotArchived();
        var credit = _credits.FirstOrDefault(c => c.PersonId == personId && c.Role == role)
                     ?? throw new DomainException("Такого запису в титрах немає.");
        _credits.Remove(credit);
        Touch();
    }

    /// <summary>Архівування (м'яке видалення): фільм зникає з каталогу, але історія зберігається.</summary>
    public void Archive()
    {
        if (IsArchived) throw new DomainException("Фільм уже в архіві.");
        IsArchived = true;
        Touch();
    }

    private void EnsureNotArchived()
    {
        if (IsArchived) throw new DomainException("Архівний фільм змінювати не можна.");
    }

    private void Touch() => UpdatedAt = DateTime.UtcNow;
}
