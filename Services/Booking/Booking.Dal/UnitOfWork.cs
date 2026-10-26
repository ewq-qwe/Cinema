using System.Data;
using Booking.Dal.Abstractions;
using Booking.Dal.Repositories;
using Microsoft.Data.SqlClient;

namespace Booking.Dal;

/// <summary>
/// Unit of Work на ADO.NET: одне з'єднання на весь HTTP-запит (Scoped), одна транзакція на бізнес-операцію.
/// Усі репозиторії отримують ТЕ САМЕ з'єднання і доступ до ПОТОЧНОЇ транзакції через делегат,
/// тому зміни кількох репозиторіїв або разом фіксуються (Commit), або разом відкочуються (Rollback).
/// З'єднання повертається в пул SqlClient у DisposeAsync.
/// </summary>
public sealed class UnitOfWork : IUnitOfWork
{
    private readonly SqlConnection _connection;
    private SqlTransaction? _transaction;

    public UnitOfWork(string connectionString)
    {
        _connection = new SqlConnection(connectionString);

        SqlTransaction? Current() => _transaction;
        Customers = new CustomerRepository(_connection, Current);
        Sessions = new SessionRepository(_connection, Current);
        Bookings = new BookingRepository(_connection, Current);
        Payments = new PaymentRepository(_connection, Current);
    }

    public ICustomerRepository Customers { get; }
    public ISessionRepository Sessions { get; }
    public IBookingRepository Bookings { get; }
    public IPaymentRepository Payments { get; }

    public bool HasActiveTransaction => _transaction is not null;

    public async Task BeginTransactionAsync(IsolationLevel level = IsolationLevel.ReadCommitted, CancellationToken ct = default)
    {
        if (_transaction is not null)
            throw new InvalidOperationException("Транзакцію вже розпочато.");
        if (_connection.State != ConnectionState.Open)
            await _connection.OpenAsync(ct);
        _transaction = (SqlTransaction)await _connection.BeginTransactionAsync(level, ct);
    }

    public async Task CommitAsync(CancellationToken ct = default)
    {
        if (_transaction is null)
            throw new InvalidOperationException("Немає активної транзакції.");
        await _transaction.CommitAsync(ct);
        await _transaction.DisposeAsync();
        _transaction = null;
    }

    public async Task RollbackAsync(CancellationToken ct = default)
    {
        if (_transaction is null) return;
        // Відкат не скасовуємо токеном: навіть якщо клієнт обірвав запит, транзакцію треба закрити
        await _transaction.RollbackAsync(CancellationToken.None);
        await _transaction.DisposeAsync();
        _transaction = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_transaction is not null)
            await _transaction.DisposeAsync();   // незафіксована транзакція відкочується
        await _connection.DisposeAsync();
    }
}
