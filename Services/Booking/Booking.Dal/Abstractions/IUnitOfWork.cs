using System.Data;

namespace Booking.Dal.Abstractions;

/// <summary>
/// Одне з'єднання і одна транзакція на бізнес-операцію (реєструється Scoped — один екземпляр на HTTP-запит).
/// Усі репозиторії отримують те саме з'єднання й поточну транзакцію.
/// </summary>
public interface IUnitOfWork : IAsyncDisposable
{
    ICustomerRepository Customers { get; }
    ISessionRepository Sessions { get; }
    IBookingRepository Bookings { get; }
    IPaymentRepository Payments { get; }

    bool HasActiveTransaction { get; }

    Task BeginTransactionAsync(IsolationLevel level = IsolationLevel.ReadCommitted, CancellationToken ct = default);
    Task CommitAsync(CancellationToken ct = default);
    Task RollbackAsync(CancellationToken ct = default);
}
