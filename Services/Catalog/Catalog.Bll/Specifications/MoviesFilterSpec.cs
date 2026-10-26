using Ardalis.Specification;
using Catalog.Bll.Dtos;
using Catalog.Domain.Entities;

namespace Catalog.Bll.Specifications;

/// <summary>
/// Специфікація списку фільмів: Include + Where + OrderBy + Skip/Take.
/// Не залежить від EF — репозиторій лише застосовує її (WithSpecification), і БД виконує один запит.
/// Сортування — лише за дозволеними полями (білий список, решта відхиляється валідатором).
/// </summary>
public sealed class MoviesFilterSpec : Specification<Movie>
{
    public MoviesFilterSpec(MovieQuery q, int page, int pageSize)
    {
        Query.Include(m => m.Genre).AsNoTracking();

        if (!string.IsNullOrWhiteSpace(q.Search))
            Query.Where(m => m.Title.Contains(q.Search));
        if (q.GenreId is not null)
            Query.Where(m => m.GenreId == q.GenreId);
        if (q.YearFrom is not null)
            Query.Where(m => m.ReleaseYear >= q.YearFrom);
        if (q.YearTo is not null)
            Query.Where(m => m.ReleaseYear <= q.YearTo);
        if (!string.IsNullOrWhiteSpace(q.AgeRating))
            Query.Where(m => m.AgeRating == q.AgeRating);

        var desc = string.Equals(q.SortDir, "desc", StringComparison.OrdinalIgnoreCase);
        switch (q.SortBy.ToLowerInvariant())
        {
            case "year":
                (desc ? Query.OrderByDescending(m => m.ReleaseYear) : Query.OrderBy(m => m.ReleaseYear)).ThenBy(m => m.Id);
                break;
            case "duration":
                (desc ? Query.OrderByDescending(m => m.DurationMinutes) : Query.OrderBy(m => m.DurationMinutes)).ThenBy(m => m.Id);
                break;
            default:
                (desc ? Query.OrderByDescending(m => m.Title) : Query.OrderBy(m => m.Title)).ThenBy(m => m.Id);
                break;
        }

        Query.Skip((page - 1) * pageSize).Take(pageSize);
    }
}
