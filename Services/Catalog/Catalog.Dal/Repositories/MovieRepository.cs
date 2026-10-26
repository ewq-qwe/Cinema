using Catalog.Dal.Abstractions;
using Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Dal.Repositories;

/// <summary>Фільми — EAGER LOADING.</summary>
public sealed class MovieRepository(CatalogDbContext context) : GenericRepository<Movie>(context), IMovieRepository
{
    /// <summary>
    /// Картка фільму: жанр, опис (1:1) і титри з людьми (M:N) завантажуються разом з фільмом
    /// ОДНИМ SQL-запитом із JOIN (Include / ThenInclude).
    /// </summary>
    public Task<Movie?> GetWithDetailsAsync(int id, CancellationToken ct = default) =>
        Set.Include(m => m.Genre)
           .Include(m => m.Detail)
           .Include(m => m.Credits).ThenInclude(c => c.Person)
           .FirstOrDefaultAsync(m => m.Id == id, ct);

    /// <summary>Унікальність «назва + рік» перевіряється і серед архівних (так само працює UNIQUE-індекс).</summary>
    public Task<bool> ExistsAsync(string title, int releaseYear, int? exceptId = null, CancellationToken ct = default) =>
        Set.IgnoreQueryFilters()
           .AnyAsync(m => m.Title == title && m.ReleaseYear == releaseYear && (exceptId == null || m.Id != exceptId), ct);

    public void SetExpectedRowVersion(Movie movie, byte[] rowVersion) =>
        Context.Entry(movie).Property(m => m.RowVersion).OriginalValue = rowVersion;
}
