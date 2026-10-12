namespace Catalog.Data;

/// <summary>Аудитні колонки — заповнюються автоматично в CatalogDbContext.SaveChanges.</summary>
public abstract class AuditableEntity
{
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

/// <summary>Жанр. 1:N з <see cref="Movie"/>.</summary>
public class Genre : AuditableEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";

    public List<Movie> Movies { get; set; } = [];
}

/// <summary>Фільм — корінь агрегату каталогу. Власник даних про фільми для всієї системи.</summary>
public class Movie : AuditableEntity
{
    public long Id { get; set; }
    public int GenreId { get; set; }
    public Genre Genre { get; set; } = null!;

    public string Title { get; set; } = "";
    public int ReleaseYear { get; set; }
    public int DurationMinutes { get; set; }
    public string AgeRating { get; set; } = "0+";

    public bool IsDeleted { get; set; }                           // soft delete
    public byte[] RowVersion { get; set; } = [];                  // оптимістична конкурентність

    public MovieDetail? Detail { get; set; }                      // 1:1
    public List<MovieCredit> Credits { get; set; } = [];          // M:N з Person
}

/// <summary>Розширений опис фільму. 1:1 з <see cref="Movie"/> (PK = FK).</summary>
public class MovieDetail
{
    public long MovieId { get; set; }
    public Movie Movie { get; set; } = null!;

    public string Description { get; set; } = "";
    public string Country { get; set; } = "";
    public string? TrailerUrl { get; set; }
    public string? PosterUrl { get; set; }
}

/// <summary>Людина з команди фільму: актор або режисер.</summary>
public class Person : AuditableEntity
{
    public int Id { get; set; }
    public string FullName { get; set; } = "";
    public int? BirthYear { get; set; }

    public List<MovieCredit> Credits { get; set; } = [];
}

/// <summary>
/// Проміжна сутність M:N Movie ↔ Person з ВЛАСНИМИ полями:
/// роль у фільмі, ім'я персонажа, порядок у титрах.
/// </summary>
public class MovieCredit
{
    public long MovieId { get; set; }
    public Movie Movie { get; set; } = null!;
    public int PersonId { get; set; }
    public Person Person { get; set; } = null!;

    public string Role { get; set; } = "Actor";                   // Actor | Director
    public string? CharacterName { get; set; }
    public int BillingOrder { get; set; }
}
