using System.Data;
using Booking.Dal.Abstractions;
using Booking.Domain;
using Microsoft.Data.SqlClient;

namespace Booking.Dal.Repositories;

/// <summary>
/// Глядачі на ЧИСТОМУ ADO.NET (лише Microsoft.Data.SqlClient): SqlCommand, типізовані параметри,
/// ручне читання через SqlDataReader, участь у транзакції Unit of Work.
/// Свідомо не успадковує Generic Repository (див. ICustomerRepository).
/// </summary>
public sealed class CustomerRepository(SqlConnection connection, Func<SqlTransaction?> currentTransaction)
    : RepositoryBase(connection, currentTransaction), ICustomerRepository
{
    private const string Columns = "Id, FullName, Email, Phone, CreatedAt, UpdatedAt, IsDeleted, RowVersion";

    public Task<Customer?> GetByIdAsync(long id, CancellationToken ct = default) =>
        SqlErrorTranslator.Run(async () =>
        {
            await EnsureOpenAsync(ct);
            await using var command = CreateCommand(
                $"SELECT {Columns} FROM dbo.Customers WHERE Id = @Id AND IsDeleted = 0");
            command.Parameters.Add("@Id", SqlDbType.BigInt).Value = id;

            await using var reader = await command.ExecuteReaderAsync(ct);
            return await reader.ReadAsync(ct) ? Map(reader) : null;
        });

    public Task<PagedResult<Customer>> SearchAsync(string? term, int page, int pageSize, CancellationToken ct = default) =>
        SqlErrorTranslator.Run(async () =>
        {
            await EnsureOpenAsync(ct);
            // Два набори результатів одним зверненням: загальна кількість + сторінка
            await using var command = CreateCommand($"""
                SELECT COUNT(*) FROM dbo.Customers
                WHERE IsDeleted = 0 AND (@Term IS NULL OR FullName LIKE @Like OR Email LIKE @Like);

                SELECT {Columns} FROM dbo.Customers
                WHERE IsDeleted = 0 AND (@Term IS NULL OR FullName LIKE @Like OR Email LIKE @Like)
                ORDER BY FullName, Id
                OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
                """);
            command.Parameters.Add("@Term", SqlDbType.NVarChar, 150).Value = (object?)term ?? DBNull.Value;
            command.Parameters.Add("@Like", SqlDbType.NVarChar, 160).Value = $"%{term}%";
            command.Parameters.Add("@Skip", SqlDbType.Int).Value = (page - 1) * pageSize;
            command.Parameters.Add("@Take", SqlDbType.Int).Value = pageSize;

            await using var reader = await command.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);
            var total = reader.GetInt32(0);

            await reader.NextResultAsync(ct);
            var items = new List<Customer>();
            while (await reader.ReadAsync(ct))
                items.Add(Map(reader));

            return new PagedResult<Customer>(items, page, pageSize, total);
        });

    public Task<long> AddAsync(Customer customer, string? userName, CancellationToken ct = default) =>
        SqlErrorTranslator.Run(async () =>
        {
            await EnsureOpenAsync(ct);
            await using var command = CreateCommand("""
                INSERT INTO dbo.Customers (FullName, Email, Phone, CreatedBy)
                OUTPUT INSERTED.Id
                VALUES (@FullName, @Email, @Phone, @UserName)
                """);
            AddCustomerParameters(command, customer, userName);
            return (long)(await command.ExecuteScalarAsync(ct))!;
        });

    public Task<bool> UpdateAsync(Customer customer, string? userName, CancellationToken ct = default) =>
        SqlErrorTranslator.Run(async () =>
        {
            await EnsureOpenAsync(ct);
            // Оптимістична конкурентність: оновлюємо лише ту версію рядка, яку бачив клієнт
            await using var command = CreateCommand("""
                UPDATE dbo.Customers
                SET FullName = @FullName, Email = @Email, Phone = @Phone,
                    UpdatedAt = SYSUTCDATETIME(), UpdatedBy = @UserName
                WHERE Id = @Id AND RowVersion = @RowVersion AND IsDeleted = 0
                """);
            AddCustomerParameters(command, customer, userName);
            command.Parameters.Add("@Id", SqlDbType.BigInt).Value = customer.Id;
            command.Parameters.Add("@RowVersion", SqlDbType.Timestamp).Value = customer.RowVersion;
            return await command.ExecuteNonQueryAsync(ct) == 1;
        });

    public Task<bool> SoftDeleteAsync(long id, string? userName, CancellationToken ct = default) =>
        SqlErrorTranslator.Run(async () =>
        {
            await EnsureOpenAsync(ct);
            await using var command = CreateCommand("""
                UPDATE dbo.Customers
                SET IsDeleted = 1, UpdatedAt = SYSUTCDATETIME(), UpdatedBy = @UserName
                WHERE Id = @Id AND IsDeleted = 0
                """);
            command.Parameters.Add("@Id", SqlDbType.BigInt).Value = id;
            command.Parameters.Add("@UserName", SqlDbType.NVarChar, 100).Value = (object?)userName ?? DBNull.Value;
            return await command.ExecuteNonQueryAsync(ct) == 1;
        });

    /// <summary>Команда на спільному з'єднанні UoW і в його поточній транзакції (якщо вона є).</summary>
    private SqlCommand CreateCommand(string sql) =>
        new(sql, Connection, Transaction) { CommandTimeout = CommandTimeoutSeconds };

    private static void AddCustomerParameters(SqlCommand command, Customer c, string? userName)
    {
        command.Parameters.Add("@FullName", SqlDbType.NVarChar, 150).Value = c.FullName;
        command.Parameters.Add("@Email", SqlDbType.NVarChar, 254).Value = c.Email;
        command.Parameters.Add("@Phone", SqlDbType.VarChar, 13).Value = (object?)c.Phone ?? DBNull.Value;
        command.Parameters.Add("@UserName", SqlDbType.NVarChar, 100).Value = (object?)userName ?? DBNull.Value;
    }

    /// <summary>Ручне читання рядка — без ORM.</summary>
    private static Customer Map(SqlDataReader r) => new()
    {
        Id = r.GetInt64(0),
        FullName = r.GetString(1),
        Email = r.GetString(2),
        Phone = r.IsDBNull(3) ? null : r.GetString(3),
        CreatedAt = r.GetDateTime(4),
        UpdatedAt = r.IsDBNull(5) ? null : r.GetDateTime(5),
        IsDeleted = r.GetBoolean(6),
        RowVersion = r.GetFieldValue<byte[]>(7)
    };
}
