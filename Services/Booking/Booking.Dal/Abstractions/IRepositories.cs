using Booking.Domain;

namespace Booking.Dal.Abstractions;

/// <summary>
/// Узагальнений CRUD-контракт. Методи повертають моделі, а не SqlDataReader / DataTable.
/// Реалізується один раз у <see cref="Repositories.BaseDapperRepository{T}"/>.
/// </summary>
public interface IGenericRepository<T> where T : class
{
    Task<T?> GetByIdAsync(long id, CancellationToken ct = default);
    Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default);
    Task<long> AddAsync(T entity, CancellationToken ct = default);
    Task<bool> UpdateAsync(T entity, CancellationToken ct = default);
    Task<bool> DeleteAsync(long id, CancellationToken ct = default);
}

/// <summary>
/// Глядачі — репозиторій на чистому ADO.NET. Свідомо НЕ успадковує <see cref="IGenericRepository{T}"/>:
/// глядач не видаляється фізично (лише soft delete), а оновлення завжди з перевіркою RowVersion,
/// тож узагальнені Delete/Update тут були б неправильними.
/// </summary>
public interface ICustomerRepository
{
    Task<Customer?> GetByIdAsync(long id, CancellationToken ct = default);
    Task<PagedResult<Customer>> SearchAsync(string? term, int page, int pageSize, CancellationToken ct = default);
    Task<long> AddAsync(Customer customer, string? userName, CancellationToken ct = default);
    /// <returns>false — рядок змінив хтось інший (RowVersion не збігся) або його не існує.</returns>
    Task<bool> UpdateAsync(Customer customer, string? userName, CancellationToken ct = default);
    Task<bool> SoftDeleteAsync(long id, string? userName, CancellationToken ct = default);
}

/// <summary>Сеанси (Dapper). Додає до CRUD читання з JOIN і схему залу.</summary>
public interface ISessionRepository : IGenericRepository<Session>
{
    /// <summary>Сеанс разом із фільмом і залом (multi-mapping).</summary>
    Task<Session?> GetWithMovieAsync(long id, CancellationToken ct = default);
    Task<IReadOnlyList<Session>> GetUpcomingAsync(long? movieId, CancellationToken ct = default);
    Task<IReadOnlyList<SeatAvailability>> GetSeatMapAsync(long sessionId, CancellationToken ct = default);
    Task<IReadOnlyList<Seat>> GetSeatsAsync(int hallId, IReadOnlyCollection<int> seatIds, CancellationToken ct = default);
    Task<bool> HasActiveTicketsAsync(long sessionId, CancellationToken ct = default);
    Task<bool> MovieExistsAsync(long movieId, CancellationToken ct = default);
    Task<bool> HallExistsAsync(int hallId, CancellationToken ct = default);
}

/// <summary>Бронювання та квитки (Dapper). Включає виклик збереженої процедури з Лаб. 01.</summary>
public interface IBookingRepository : IGenericRepository<Domain.Booking>
{
    /// <summary>Бронювання + квитки + оплата одним зверненням (QueryMultiple + multi-mapping).</summary>
    Task<Domain.Booking?> GetWithDetailsAsync(long id, CancellationToken ct = default);
    /// <summary>Читання з блокуванням рядка (UPDLOCK) до кінця транзакції — песимістична конкурентність.</summary>
    Task<Domain.Booking?> GetForUpdateAsync(long id, CancellationToken ct = default);
    Task<PagedResult<Domain.Booking>> GetByCustomerAsync(long customerId, int page, int pageSize, CancellationToken ct = default);
    Task AddTicketsAsync(IEnumerable<Ticket> tickets, CancellationToken ct = default);
    Task<IReadOnlyList<int>> GetTakenSeatIdsAsync(long sessionId, IReadOnlyCollection<int> seatIds, CancellationToken ct = default);
    Task<bool> MarkPaidAsync(long id, string? userName, CancellationToken ct = default);
    /// <summary>dbo.usp_Booking_ChangeStatus: перевірка допустимого переходу в БД (THROW 50002 → 409).</summary>
    Task ChangeStatusAsync(long id, string newStatus, string? reason, byte[]? expectedRowVersion, string? userName, CancellationToken ct = default);
    Task<bool> HasActiveBookingsAsync(long customerId, CancellationToken ct = default);
}

/// <summary>Оплати бронювань (Dapper). Ключ — BookingId (зв'язок 1:1).</summary>
public interface IPaymentRepository : IGenericRepository<BookingPayment>
{
    Task<BookingPayment?> GetByTransactionIdAsync(string transactionId, CancellationToken ct = default);
}

/// <summary>Лаб. 08: знімок фільму з події Каталогу — те, що зберігає локальна копія dbo.Movies.</summary>
public sealed record MovieSnapshot(long Id, string Title, int DurationMinutes, string AgeRating, bool IsArchived = false);

/// <summary>
/// Лаб. 08: локальна копія довідника фільмів (read model), яку оновлюють лише події Каталогу.
/// Джерело істини — Каталог; тут лише копія для власних запитів (розклад, бронювання).
/// </summary>
public interface IMovieCopyRepository
{
    /// <summary>Створити або оновити копію. false — подія застаріла (у копії вже новіші дані), нічого не змінено.</summary>
    Task<bool> UpsertAsync(MovieSnapshot movie, DateTime occurredAt, CancellationToken ct = default);

    /// <summary>Позначити фільм архівним. false — фільму немає в копії або подія застаріла.</summary>
    Task<bool> ArchiveAsync(long id, DateTime occurredAt, CancellationToken ct = default);

    Task<MovieSnapshot?> GetAsync(long id, CancellationToken ct = default);
}

/// <summary>Лаб. 08: журнал оброблених подій для ідемпотентності споживача (dbo.ProcessedEvents).</summary>
public interface IProcessedEventRepository
{
    /// <summary>Записати EventId як оброблений. false — подію вже оброблено (дублікат), обробку треба пропустити.</summary>
    Task<bool> TryAddAsync(Guid eventId, string eventType, CancellationToken ct = default);
}