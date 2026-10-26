using Catalog.Domain.Entities;

namespace Catalog.Dal.Abstractions;

/// <summary>
/// Набір репозиторіїв над одним DbContext і єдиний SaveChangesAsync: усі зміни бізнес-операції
/// потрапляють у БД однією транзакцією (як IUnitOfWork у eShop, де його реалізує DbContext).
/// </summary>
public interface IUnitOfWork
{
    IMovieRepository Movies { get; }
    IGenreRepository Genres { get; }
    IMovieCreditRepository MovieCredits { get; }
    IGenericRepository<Person> People { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
