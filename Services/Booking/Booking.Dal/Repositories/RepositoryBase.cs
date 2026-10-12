using System.Data;
using Microsoft.Data.SqlClient;

namespace Booking.Dal.Repositories;

/// <summary>
/// Спільна база всіх репозиторіїв: отримує НЕ рядок підключення, а з'єднання Unit of Work
/// і доступ до його поточної транзакції — так кілька репозиторіїв працюють в одній транзакції.
/// </summary>
public abstract class RepositoryBase
{
    /// <summary>Таймаут команди, с. Довгі блокування краще обірвати, ніж тримати з'єднання з пулу.</summary>
    protected const int CommandTimeoutSeconds = 30;

    protected readonly SqlConnection Connection;
    private readonly Func<SqlTransaction?> _currentTransaction;

    protected RepositoryBase(SqlConnection connection, Func<SqlTransaction?> currentTransaction)
    {
        Connection = connection;
        _currentTransaction = currentTransaction;
    }

    /// <summary>Поточна транзакція Unit of Work (або null, якщо її не розпочато).</summary>
    protected SqlTransaction? Transaction => _currentTransaction();

    protected async Task EnsureOpenAsync(CancellationToken ct)
    {
        if (Connection.State != ConnectionState.Open)
            await Connection.OpenAsync(ct);
    }
}
