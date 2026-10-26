namespace Catalog.Domain.Entities;

public enum CreditRole
{
    Actor = 1,
    Director = 2
}

/// <summary>
/// Проміжна сутність M:N Movie ↔ Person з ВЛАСНИМИ полями: роль, ім'я персонажа, порядок у титрах
/// і дата додавання в титри (CreditedAt). Автоматичне M:N в EF Core цих полів зберегти не дало б.
/// Створюється лише через <see cref="Movie.AddCredit"/>, де перевіряються правила.
/// </summary>
public class MovieCredit
{
    public const int CharacterNameMaxLength = 100;

    public int MovieId { get; private set; }
    public Movie Movie { get; private set; } = null!;
    public int PersonId { get; private set; }
    public Person Person { get; private set; } = null!;

    public CreditRole Role { get; private set; }
    public string? CharacterName { get; private set; }
    public int BillingOrder { get; private set; }
    public DateTime CreditedAt { get; private set; }

    private MovieCredit() { } // для EF Core

    internal MovieCredit(Movie movie, Person person, CreditRole role, string? characterName, int billingOrder)
    {
        Movie = movie;
        MovieId = movie.Id;
        Person = person;
        PersonId = person.Id;
        Role = role;
        CharacterName = characterName;
        BillingOrder = billingOrder;
        CreditedAt = DateTime.UtcNow;
    }
}
