using System.ComponentModel.DataAnnotations;

namespace Catalog.Bll.Dtos;

// =====================================================================
// DTO — лише дані API-контрактів. Прості правила (Required / StringLength / Range) — DataAnnotations тут;
// складні правила (залежності між полями, допустимі значення, формати) — валідатори FluentValidation
// у Validators/. Правила не дублюються.
// =====================================================================

// ---------- Фільми ----------

public sealed class MovieListItemDto
{
    public int Id { get; init; }
    public string Title { get; init; } = "";
    public int ReleaseYear { get; init; }
    public int DurationMinutes { get; init; }
    public string AgeRating { get; init; } = "";
    public int GenreId { get; init; }
    public string GenreName { get; init; } = "";
}

public sealed class MovieDto
{
    public int Id { get; init; }
    public string Title { get; init; } = "";
    public int ReleaseYear { get; init; }
    public int DurationMinutes { get; init; }
    public string AgeRating { get; init; } = "";
    public int GenreId { get; init; }
    public string GenreName { get; init; } = "";
    public string? Description { get; init; }
    public string? Country { get; init; }
    public string? TrailerUrl { get; init; }
    public string? PosterUrl { get; init; }
    public decimal? Budget { get; init; }
    public IReadOnlyList<CreditDto> Credits { get; init; } = [];
    /// <summary>Версія рядка (base64) — передайте її в PUT для виявлення конфлікту паралельних змін.</summary>
    public string RowVersion { get; init; } = "";
}

public sealed class CreditDto
{
    public int PersonId { get; init; }
    public string PersonFullName { get; init; } = "";
    public string Role { get; init; } = "";
    public string? CharacterName { get; init; }
    public int BillingOrder { get; init; }
    public DateTime CreditedAt { get; init; }
}

public class CreateMovieDto
{
    [Required, StringLength(200)]
    public string Title { get; set; } = "";

    public int ReleaseYear { get; set; }

    public int DurationMinutes { get; set; }

    [Required, StringLength(5)]
    public string AgeRating { get; set; } = "";

    [Range(1, int.MaxValue)]
    public int GenreId { get; set; }
}

public sealed class UpdateMovieDto : CreateMovieDto
{
    [Required]
    public string RowVersion { get; set; } = "";
}

public sealed class SetMovieDetailDto
{
    [Required, StringLength(2000)]
    public string Description { get; set; } = "";

    [Required, StringLength(60)]
    public string Country { get; set; } = "";

    [StringLength(300)]
    public string? TrailerUrl { get; set; }

    [StringLength(300)]
    public string? PosterUrl { get; set; }

    public decimal? Budget { get; set; }
}

public sealed class AddCreditDto
{
    [Range(1, int.MaxValue)]
    public int PersonId { get; set; }

    /// <summary>Actor або Director.</summary>
    [Required]
    public string Role { get; set; } = "";

    [StringLength(100)]
    public string? CharacterName { get; set; }

    public int BillingOrder { get; set; }
}

/// <summary>Параметри списку фільмів: пагінація, фільтри, сортування (виконуються в SQL).</summary>
public sealed class MovieQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    /// <summary>Пошук за частиною назви.</summary>
    public string? Search { get; set; }
    public int? GenreId { get; set; }
    public int? YearFrom { get; set; }
    public int? YearTo { get; set; }
    public string? AgeRating { get; set; }
    /// <summary>title | year | duration</summary>
    public string SortBy { get; set; } = "title";
    /// <summary>asc | desc</summary>
    public string SortDir { get; set; } = "asc";
}

// ---------- Жанри ----------

public sealed class GenreDto
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
}

public sealed class GenreWithMoviesDto
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public IReadOnlyList<MovieShortDto> Movies { get; init; } = [];
}

public sealed class MovieShortDto
{
    public int Id { get; init; }
    public string Title { get; init; } = "";
    public int ReleaseYear { get; init; }
}

public sealed class CreateGenreDto
{
    [Required, StringLength(50, MinimumLength = 2)]
    public string Name { get; set; } = "";
}

// ---------- Люди ----------

public sealed class PersonDto
{
    public int Id { get; init; }
    public string FullName { get; init; } = "";
    public int? BirthYear { get; init; }
}

public sealed class CreatePersonDto
{
    [Required, StringLength(150, MinimumLength = 2)]
    public string FullName { get; set; } = "";

    public int? BirthYear { get; set; }
}

public sealed class FilmographyItemDto
{
    public int MovieId { get; init; }
    public string Title { get; init; } = "";
    public int ReleaseYear { get; init; }
    public string GenreName { get; init; } = "";
    public string Role { get; init; } = "";
    public string? CharacterName { get; init; }
    public int BillingOrder { get; init; }
    public DateTime CreditedAt { get; init; }
}

/// <summary>Параметри списку людей.</summary>
public sealed class PersonQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public string? Search { get; set; }
    public int? BornFrom { get; set; }
    public int? BornTo { get; set; }
    /// <summary>name | birthYear</summary>
    public string SortBy { get; set; } = "name";
    /// <summary>asc | desc</summary>
    public string SortDir { get; set; } = "asc";
}

// ---------- Пагінація ----------

/// <summary>Сторінка результатів. Метадані — у тілі відповіді (і дублюються в заголовках X-Pagination-*).</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasNext => Page < TotalPages;
    public bool HasPrevious => Page > 1;
}
