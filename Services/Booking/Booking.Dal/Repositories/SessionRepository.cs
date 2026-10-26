using Booking.Dal.Abstractions;
using Booking.Domain;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Booking.Dal.Repositories;

/// <summary>Сеанси на Dapper: CRUD успадковано з BaseDapperRepository, тут — лише специфічні запити.</summary>
public sealed class SessionRepository(SqlConnection connection, Func<SqlTransaction?> currentTransaction)
    : BaseDapperRepository<Session>(connection, currentTransaction,
        table: "dbo.Sessions", key: "Id",
        insertColumns: ["MovieId", "HallId", "StartsAt", "BasePrice"],
        updateColumns: ["MovieId", "HallId", "StartsAt", "BasePrice", "UpdatedAt"]),
      ISessionRepository
{
    private const string SessionWithMovieSql = """
        SELECT s.Id, s.MovieId, s.HallId, s.StartsAt, s.BasePrice, s.CreatedAt, s.UpdatedAt, h.Name AS HallName,
               m.Id, m.Title, m.DurationMinutes, m.AgeRating
        FROM dbo.Sessions s
        JOIN dbo.Halls  h ON h.Id = s.HallId
        JOIN dbo.Movies m ON m.Id = s.MovieId
        """;

    protected override string SelectColumns => "Id, MovieId, HallId, StartsAt, BasePrice, CreatedAt, UpdatedAt";

    /// <summary>Multi-mapping: один рядок JOIN → Session + Movie (розділ на стовпці «Id» фільму).</summary>
    public Task<Session?> GetWithMovieAsync(long id, CancellationToken ct = default) =>
        SqlErrorTranslator.Run(async () =>
        {
            await EnsureOpenAsync(ct);
            var rows = await Connection.QueryAsync<Session, Movie, Session>(
                Cmd(SessionWithMovieSql + " WHERE s.Id = @Id", new { Id = id }, ct),
                (session, movie) => { session.Movie = movie; return session; },
                splitOn: "Id");
            return rows.SingleOrDefault();
        });

    public Task<IReadOnlyList<Session>> GetUpcomingAsync(long? movieId, CancellationToken ct = default) =>
        SqlErrorTranslator.Run<IReadOnlyList<Session>>(async () =>
        {
            await EnsureOpenAsync(ct);
            var rows = await Connection.QueryAsync<Session, Movie, Session>(
                Cmd(SessionWithMovieSql + """
                     WHERE s.StartsAt > SYSUTCDATETIME() AND (@MovieId IS NULL OR s.MovieId = @MovieId)
                    ORDER BY s.StartsAt
                    """, new { MovieId = movieId }, ct),
                (session, movie) => { session.Movie = movie; return session; },
                splitOn: "Id");
            return rows.AsList();
        });

    public Task<IReadOnlyList<SeatAvailability>> GetSeatMapAsync(long sessionId, CancellationToken ct = default) =>
        SqlErrorTranslator.Run<IReadOnlyList<SeatAvailability>>(async () =>
        {
            await EnsureOpenAsync(ct);
            var rows = await Connection.QueryAsync<SeatAvailability>(Cmd("""
                SELECT s.Id AS SeatId, s.RowNumber, s.SeatNumber, s.SeatType,
                       CAST(CASE WHEN t.SeatId IS NULL THEN 1 ELSE 0 END AS BIT) AS IsFree
                FROM dbo.Sessions ss
                JOIN dbo.Seats s ON s.HallId = ss.HallId
                LEFT JOIN dbo.Tickets t ON t.SessionId = ss.Id AND t.SeatId = s.Id AND t.IsCancelled = 0
                WHERE ss.Id = @SessionId
                ORDER BY s.RowNumber, s.SeatNumber
                """, new { SessionId = sessionId }, ct));
            return rows.AsList();
        });

    /// <summary>Місця з переліку, що належать залу. Dapper розгортає список у IN (@p1, @p2, …) — параметризовано.</summary>
    public Task<IReadOnlyList<Seat>> GetSeatsAsync(int hallId, IReadOnlyCollection<int> seatIds, CancellationToken ct = default) =>
        SqlErrorTranslator.Run<IReadOnlyList<Seat>>(async () =>
        {
            await EnsureOpenAsync(ct);
            var rows = await Connection.QueryAsync<Seat>(Cmd("""
                SELECT Id, HallId, RowNumber, SeatNumber, SeatType
                FROM dbo.Seats
                WHERE HallId = @HallId AND Id IN @SeatIds
                """, new { HallId = hallId, SeatIds = seatIds }, ct));
            return rows.AsList();
        });

    public Task<bool> HasActiveTicketsAsync(long sessionId, CancellationToken ct = default) =>
        ExistsAsync("SELECT COUNT(1) FROM dbo.Tickets WHERE SessionId = @Id AND IsCancelled = 0", sessionId, ct);

    public Task<bool> MovieExistsAsync(long movieId, CancellationToken ct = default) =>
        ExistsAsync("SELECT COUNT(1) FROM dbo.Movies WHERE Id = @Id", movieId, ct);

    public Task<bool> HallExistsAsync(int hallId, CancellationToken ct = default) =>
        ExistsAsync("SELECT COUNT(1) FROM dbo.Halls WHERE Id = @Id AND IsActive = 1", hallId, ct);

    private Task<bool> ExistsAsync(string sql, long id, CancellationToken ct) =>
        SqlErrorTranslator.Run(async () =>
        {
            await EnsureOpenAsync(ct);
            return await Connection.ExecuteScalarAsync<int>(Cmd(sql, new { Id = id }, ct)) > 0;
        });
}
