using Catalog.Dal.Abstractions;
using Catalog.Dal.ReadModels;
using Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Dal.Repositories;

/// <summary>Участь у фільмах — LINQ to Entities через проміжну сутність M:N Movie ↔ Person.</summary>
public sealed class MovieCreditRepository(CatalogDbContext context)
    : GenericRepository<MovieCredit>(context), IMovieCreditRepository
{
    /// <summary>Ключ MovieCredit складений (MovieId, PersonId, Role) — пошук за одним int не має сенсу.</summary>
    public override Task<MovieCredit?> GetByIdAsync(int id, CancellationToken ct = default) =>
        throw new NotSupportedException("MovieCredit має складений ключ (MovieId, PersonId, Role).");

    /// <summary>
    /// Фільмографія людини: вибірка йде від проміжної сутності MovieCredit до Movie і Genre,
    /// разом із власними полями зв'язку (роль, персонаж, порядок у титрах, дата додавання).
    /// </summary>
    public async Task<IReadOnlyList<FilmographyEntry>> GetFilmographyAsync(int personId, CancellationToken ct = default) =>
        await Set.AsNoTracking()
            .Where(c => c.PersonId == personId)
            .OrderByDescending(c => c.Movie.ReleaseYear).ThenBy(c => c.Role).ThenBy(c => c.BillingOrder)
            .Select(c => new FilmographyEntry(
                c.MovieId, c.Movie.Title, c.Movie.ReleaseYear, c.Movie.Genre.Name,
                c.Role, c.CharacterName, c.BillingOrder, c.CreditedAt))
            .ToListAsync(ct);

    /// <summary>
    /// Актори головних ролей: фільтр за ВЛАСНИМИ полями проміжної сутності (Role, BillingOrder)
    /// і за жанром фільму на іншому кінці зв'язку; результат — люди, без дублікатів.
    /// </summary>
    public async Task<IReadOnlyList<Person>> GetLeadActorsAsync(string? genreName, int maxBillingOrder, CancellationToken ct = default) =>
        await Set.AsNoTracking()
            .Where(c => c.Role == CreditRole.Actor && c.BillingOrder >= 1 && c.BillingOrder <= maxBillingOrder)
            .Where(c => genreName == null || c.Movie.Genre.Name == genreName)
            .Select(c => c.Person)
            .Distinct()
            .OrderBy(p => p.FullName)
            .ToListAsync(ct);
}
