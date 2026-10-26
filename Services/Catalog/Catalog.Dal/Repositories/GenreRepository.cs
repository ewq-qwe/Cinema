using Catalog.Dal.Abstractions;
using Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Dal.Repositories;

/// <summary>Жанри — EXPLICIT LOADING.</summary>
public sealed class GenreRepository(CatalogDbContext context) : GenericRepository<Genre>(context), IGenreRepository
{
    /// <summary>
    /// Жанр і його фільми ДВОМА окремими запитами: спершу сам жанр, а колекція Movies
    /// довантажується явно (Entry(...).Collection(...).LoadAsync) лише коли вона справді потрібна.
    /// </summary>
    public async Task<Genre?> GetWithMoviesAsync(int id, CancellationToken ct = default)
    {
        var genre = await Set.FirstOrDefaultAsync(g => g.Id == id, ct);          // 1-й запит
        if (genre is null) return null;

        await Context.Entry(genre)
            .Collection(g => g.Movies)
            .Query()
            .OrderByDescending(m => m.ReleaseYear).ThenBy(m => m.Title)
            .LoadAsync(ct);                                                     // 2-й запит
        return genre;
    }

    public Task<bool> ExistsByNameAsync(string name, CancellationToken ct = default) =>
        Set.AnyAsync(g => g.Name == name, ct);

    /// <summary>Враховує й архівні фільми: FK з ON DELETE RESTRICT не дасть видалити жанр із будь-якими фільмами.</summary>
    public Task<bool> HasMoviesAsync(int id, CancellationToken ct = default) =>
        Context.Movies.IgnoreQueryFilters().AnyAsync(m => m.GenreId == id, ct);
}
