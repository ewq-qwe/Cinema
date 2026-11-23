using System.Reflection;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Booking.Dal.Migrations;

/// <summary>
/// Міграції схеми CinemaBooking для змін після Лаб. 01 (схема з db/p1 створюється лише раз, при створенні тому).
/// Скрипти — вбудовані ресурси Migrations/NNN_*.sql; застосовуються по порядку номерів, кожен — один раз і в транзакції.
/// Історія — таблиця dbo.SchemaMigrations (аналог __EFMigrationsHistory для ADO.NET-сервісу).
/// </summary>
public static partial class SchemaMigrator
{
    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex BatchSeparator();

    /// <returns>Назви застосованих цього разу міграцій.</returns>
    public static async Task<IReadOnlyList<string>> MigrateAsync(string connectionString, CancellationToken ct = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition("""
            IF OBJECT_ID(N'dbo.SchemaMigrations', N'U') IS NULL
            CREATE TABLE dbo.SchemaMigrations (
                MigrationId NVARCHAR(150) NOT NULL CONSTRAINT PK_SchemaMigrations PRIMARY KEY,
                AppliedAt   DATETIME2(3)  NOT NULL CONSTRAINT DF_SchemaMigrations_AppliedAt DEFAULT (SYSUTCDATETIME())
            );
            """, cancellationToken: ct));

        var applied = (await connection.QueryAsync<string>(new CommandDefinition(
            "SELECT MigrationId FROM dbo.SchemaMigrations", cancellationToken: ct))).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var assembly = typeof(SchemaMigrator).Assembly;
        var result = new List<string>();
        foreach (var (id, resource) in Scripts(assembly).Where(s => !applied.Contains(s.Id)))
        {
            await using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);
            var script = await reader.ReadToEndAsync(ct);

            await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(ct);
            foreach (var batch in BatchSeparator().Split(script).Where(b => !string.IsNullOrWhiteSpace(b)))
                await connection.ExecuteAsync(new CommandDefinition(batch, transaction: tx, cancellationToken: ct));
            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT dbo.SchemaMigrations (MigrationId) VALUES (@id)", new { id }, tx, cancellationToken: ct));
            await tx.CommitAsync(ct);
            result.Add(id);
        }
        return result;
    }

    /// <summary>Вбудовані скрипти: Booking.Dal.Migrations.001_Lab08_CatalogSync.sql → Id "001_Lab08_CatalogSync".</summary>
    private static IEnumerable<(string Id, string Resource)> Scripts(Assembly assembly)
    {
        const string prefix = "Booking.Dal.Migrations.";
        return assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(prefix, StringComparison.Ordinal) && n.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .Select(n => (Id: n[prefix.Length..^4], Resource: n))
            .OrderBy(s => s.Id, StringComparer.Ordinal);
    }
}
