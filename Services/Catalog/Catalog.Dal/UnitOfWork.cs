using Catalog.Dal.Abstractions;
using Catalog.Dal.Repositories;
using Catalog.Domain.Entities;
using Catalog.Domain.Exceptions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Dal;

/// <summary>
/// Unit of Work на EF Core: усі репозиторії працюють з одним DbContext (Scoped — один на HTTP-запит),
/// а єдиний SaveChangesAsync наприкінці бізнес-операції зберігає всі зміни в одній транзакції.
/// Помилки БД перетворюються на доменні винятки, щоб API віддав 409, а не 500.
/// </summary>
public sealed class UnitOfWork : IUnitOfWork
{
    private readonly CatalogDbContext _context;

    public UnitOfWork(CatalogDbContext context)
    {
        _context = context;
        Movies = new MovieRepository(context);
        Genres = new GenreRepository(context);
        MovieCredits = new MovieCreditRepository(context);
        People = new GenericRepository<Person>(context);
    }

    public IMovieRepository Movies { get; }
    public IGenreRepository Genres { get; }
    public IMovieCreditRepository MovieCredits { get; }
    public IGenericRepository<Person> People { get; }

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            return await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("Запис змінено іншим користувачем. Отримайте актуальну версію й повторіть.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw new ConflictException("Запис з таким унікальним значенням уже існує.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 547 })
        {
            throw new ConflictException("Операцію відхилено: на запис посилаються інші дані або порушено обмеження.");
        }
    }
}
