using Booking.Dal.Abstractions;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Booking.Dal.Repositories;

/// <summary>
/// Лаб. 08: локальна копія довідника фільмів (dbo.Movies), яку оновлюють лише події Каталогу.
/// Знімки в бронюваннях (Bookings.MovieTitle) і ціни квитків (Tickets.Price) тут НЕ змінюються — це історія продажу.
/// </summary>
public sealed class MovieCopyRepository(SqlConnection connection, Func<SqlTransaction?> currentTransaction)
    : RepositoryBase(connection, currentTransaction), IMovieCopyRepository
{
    public async Task<bool> UpsertAsync(MovieSnapshot movie, DateTime occurredAt, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        // MERGE + HOLDLOCK: «вставити або оновити» атомарно (без гонки між двома споживачами).
        // Умова CatalogUpdatedAt <= @OccurredAt відкидає застарілу подію, що прийшла пізніше за новішу (out-of-order).
        const string sql = """
            MERGE dbo.Movies WITH (HOLDLOCK) AS t
            USING (SELECT @Id AS Id) AS s ON t.Id = s.Id
            WHEN MATCHED AND (t.CatalogUpdatedAt IS NULL OR t.CatalogUpdatedAt <= @OccurredAt) THEN
                UPDATE SET Title = @Title, DurationMinutes = @DurationMinutes, AgeRating = @AgeRating,
                           CatalogUpdatedAt = @OccurredAt, SyncedAt = SYSUTCDATETIME()
            WHEN NOT MATCHED THEN
                INSERT (Id, Title, DurationMinutes, AgeRating, CatalogUpdatedAt)
                VALUES (@Id, @Title, @DurationMinutes, @AgeRating, @OccurredAt);
            """;
        // 547 (CHECK) → BusinessConflictException: дані події несумісні з копією — постійний збій, а не тимчасовий
        var rows = await SqlErrorTranslator.Run(() => Connection.ExecuteAsync(new CommandDefinition(sql,
            new { movie.Id, movie.Title, movie.DurationMinutes, movie.AgeRating, OccurredAt = occurredAt },
            Transaction, CommandTimeoutSeconds, cancellationToken: ct)));
        return rows > 0;
    }

    public async Task<bool> ArchiveAsync(long id, DateTime occurredAt, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        const string sql = """
            UPDATE dbo.Movies
            SET IsArchived = 1, CatalogUpdatedAt = @OccurredAt, SyncedAt = SYSUTCDATETIME()
            WHERE Id = @Id AND (CatalogUpdatedAt IS NULL OR CatalogUpdatedAt <= @OccurredAt);
            """;
        return await Connection.ExecuteAsync(new CommandDefinition(sql, new { Id = id, OccurredAt = occurredAt },
            Transaction, CommandTimeoutSeconds, cancellationToken: ct)) > 0;
    }

    public async Task<MovieSnapshot?> GetAsync(long id, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        return await Connection.QuerySingleOrDefaultAsync<MovieSnapshot>(new CommandDefinition(
            "SELECT Id, Title, DurationMinutes, AgeRating, IsArchived FROM dbo.Movies WHERE Id = @Id",
            new { Id = id }, Transaction, CommandTimeoutSeconds, cancellationToken: ct));
    }
}

/// <summary>
/// Лаб. 08: оброблені integration events (ідемпотентність споживача). EventId — PRIMARY KEY dbo.ProcessedEvents;
/// запис виконується в транзакції Unit of Work разом із бізнес-змінами.
/// </summary>
public sealed class ProcessedEventRepository(SqlConnection connection, Func<SqlTransaction?> currentTransaction)
    : RepositoryBase(connection, currentTransaction), IProcessedEventRepository
{
    public async Task<bool> TryAddAsync(Guid eventId, string eventType, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        // UPDLOCK + HOLDLOCK: другий споживач з тим самим EventId чекає на коміт першого, а потім бачить запис.
        // PRIMARY KEY — остання лінія захисту: порушення (2627) теж означає «уже оброблено».
        const string sql = """
            INSERT dbo.ProcessedEvents (EventId, EventType)
            SELECT @EventId, @EventType
            WHERE NOT EXISTS (SELECT 1 FROM dbo.ProcessedEvents WITH (UPDLOCK, HOLDLOCK) WHERE EventId = @EventId);
            """;
        try
        {
            return await Connection.ExecuteAsync(new CommandDefinition(sql, new { EventId = eventId, EventType = eventType },
                Transaction, CommandTimeoutSeconds, cancellationToken: ct)) == 1;
        }
        catch (SqlException ex) when (ex.Number is 2627 or 2601)
        {
            return false;
        }
    }
}
