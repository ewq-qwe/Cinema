using Booking.Dal.Abstractions;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Booking.Dal.Repositories;

/// <summary>
/// Спільний CRUD на Dapper — написаний один раз. Конкретні репозиторії додають лише специфічні запити.
/// Ім'я таблиці, ключа та списки колонок — КОНСТАНТИ з коду похідних класів (не вхідні дані користувача),
/// тому їх підстановка в текст SQL безпечна. Усі значення передаються лише як параметри (@Name).
/// </summary>
public abstract class BaseDapperRepository<T> : RepositoryBase, IGenericRepository<T> where T : class
{
    private readonly string _table;
    private readonly string _key;
    private readonly string _insertSql;
    private readonly string _updateSql;

    protected BaseDapperRepository(
        SqlConnection connection, Func<SqlTransaction?> currentTransaction,
        string table, string key, string[] insertColumns, string[] updateColumns)
        : base(connection, currentTransaction)
    {
        _table = table;
        _key = key;
        _insertSql = $"INSERT INTO {table} ({string.Join(", ", insertColumns)}) " +
                     $"OUTPUT INSERTED.{key} " +
                     $"VALUES ({string.Join(", ", insertColumns.Select(c => "@" + c))})";
        _updateSql = $"UPDATE {table} SET {string.Join(", ", updateColumns.Select(c => $"{c} = @{c}"))} " +
                     $"WHERE {key} = @{key}";
    }

    /// <summary>Колонки для SELECT у загальних методах (похідний клас може звузити).</summary>
    protected virtual string SelectColumns => "*";

    /// <summary>Додаткова умова для «живих» записів, напр. "IsDeleted = 0".</summary>
    protected virtual string? ActiveFilter => null;

    private string Where(string condition) =>
        ActiveFilter is null ? $"WHERE {condition}" : $"WHERE {condition} AND {ActiveFilter}";

    /// <summary>CommandDefinition, прив'язаний до поточної транзакції UoW і CancellationToken.</summary>
    protected CommandDefinition Cmd(string sql, object? parameters, CancellationToken ct,
        System.Data.CommandType type = System.Data.CommandType.Text) =>
        new(sql, parameters, Transaction, CommandTimeoutSeconds, type, cancellationToken: ct);

    public virtual Task<T?> GetByIdAsync(long id, CancellationToken ct = default) =>
        SqlErrorTranslator.Run(async () =>
        {
            await EnsureOpenAsync(ct);
            return await Connection.QuerySingleOrDefaultAsync<T>(
                Cmd($"SELECT {SelectColumns} FROM {_table} {Where($"{_key} = @Id")}", new { Id = id }, ct));
        });

    public virtual Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default) =>
        SqlErrorTranslator.Run<IReadOnlyList<T>>(async () =>
        {
            await EnsureOpenAsync(ct);
            var rows = await Connection.QueryAsync<T>(
                Cmd($"SELECT {SelectColumns} FROM {_table} {(ActiveFilter is null ? "" : "WHERE " + ActiveFilter)} ORDER BY {_key}", null, ct));
            return rows.AsList();
        });

    public virtual Task<long> AddAsync(T entity, CancellationToken ct = default) =>
        SqlErrorTranslator.Run(async () =>
        {
            await EnsureOpenAsync(ct);
            return Convert.ToInt64(await Connection.ExecuteScalarAsync(Cmd(_insertSql, entity, ct)));
        });

    public virtual Task<bool> UpdateAsync(T entity, CancellationToken ct = default) =>
        SqlErrorTranslator.Run(async () =>
        {
            await EnsureOpenAsync(ct);
            return await Connection.ExecuteAsync(Cmd(_updateSql, entity, ct)) > 0;
        });

    public virtual Task<bool> DeleteAsync(long id, CancellationToken ct = default) =>
        SqlErrorTranslator.Run(async () =>
        {
            await EnsureOpenAsync(ct);
            return await Connection.ExecuteAsync(Cmd($"DELETE FROM {_table} WHERE {_key} = @Id", new { Id = id }, ct)) > 0;
        });
}
