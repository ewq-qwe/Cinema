using Ardalis.Specification;
using Catalog.Dal.ReadModels;
using Catalog.Domain.Entities;

namespace Catalog.Dal.Abstractions;

/// <summary>
/// Async Generic Repository: той самий контракт, що в Лаб. 02, але реалізований на EF Core через DbSet&lt;T&gt;.
/// Методи з доступом до БД — асинхронні; Update/Delete лише змінюють стан у контексті.
/// Репозиторії НЕ зберігають зміни — це робить Unit of Work одним SaveChangesAsync.
/// </summary>
public interface IGenericRepository<T> where T : class
{
    Task<T?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default);
    Task AddAsync(T entity, CancellationToken ct = default);
    void Update(T entity);
    void Delete(T entity);

    /// <summary>Вибірка за специфікацією (Where / Include / OrderBy / Skip / Take) одним SQL-запитом.</summary>
    Task<IReadOnlyList<T>> ListAsync(ISpecification<T> specification, CancellationToken ct = default);

    /// <summary>Кількість за критеріями специфікації (сортування й пагінація ігноруються) — COUNT у БД.</summary>
    Task<int> CountAsync(ISpecification<T> specification, CancellationToken ct = default);
}

/// <summary>Фільми. Специфічний запит — EAGER LOADING: картка фільму з усім пов'язаним ОДНИМ запитом.</summary>
public interface IMovieRepository : IGenericRepository<Movie>
{
    Task<Movie?> GetWithDetailsAsync(int id, CancellationToken ct = default);
    Task<bool> ExistsAsync(string title, int releaseYear, int? exceptId = null, CancellationToken ct = default);
    /// <summary>Версія рядка, яку бачив клієнт: якщо в БД вона інша — SaveChanges дасть конфлікт.</summary>
    void SetExpectedRowVersion(Movie movie, byte[] rowVersion);
}

/// <summary>Жанри. Специфічний запит — EXPLICIT LOADING: жанр окремо, його фільми — окремим запитом.</summary>
public interface IGenreRepository : IGenericRepository<Genre>
{
    Task<Genre?> GetWithMoviesAsync(int id, CancellationToken ct = default);
    Task<bool> ExistsByNameAsync(string name, CancellationToken ct = default);
    Task<bool> HasMoviesAsync(int id, CancellationToken ct = default);
}

/// <summary>Участь у фільмах. Специфічні запити — LINQ to Entities через проміжну сутність M:N і її власні поля.</summary>
public interface IMovieCreditRepository : IGenericRepository<MovieCredit>
{
    Task<IReadOnlyList<FilmographyEntry>> GetFilmographyAsync(int personId, CancellationToken ct = default);
    Task<IReadOnlyList<Person>> GetLeadActorsAsync(string? genreName, int maxBillingOrder, CancellationToken ct = default);
}
