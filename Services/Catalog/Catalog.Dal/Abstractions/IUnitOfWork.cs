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

    /// <summary>Лаб. 08: transactional outbox — події зберігаються тим самим SaveChangesAsync, що й бізнес-зміни.</summary>
    IIntegrationEventLogRepository IntegrationEventLog { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);

    /// <summary>
    /// Кілька SaveChangesAsync в одній явній транзакції. Потрібно, коли подія залежить від значення, яке генерує БД
    /// (Id нового фільму): спершу зберегти фільм, потім додати подію з його Id — і зафіксувати обидва разом.
    /// </summary>
    Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken ct = default);
}
