using System.Data;
using Booking.Dal.Abstractions;
using Booking.Domain;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Booking.Dal.Repositories;

/// <summary>Бронювання і квитки на Dapper: multi-mapping, QueryMultiple, виклик збереженої процедури.</summary>
public sealed class BookingRepository(SqlConnection connection, Func<SqlTransaction?> currentTransaction)
    : BaseDapperRepository<Domain.Booking>(connection, currentTransaction,
        table: "dbo.Bookings", key: "Id",
        insertColumns: ["CustomerId", "SessionId", "Status", "MovieTitle", "SessionStartsAt", "TotalAmount", "CreatedBy"],
        updateColumns: ["Status", "CancelReason", "TotalAmount", "UpdatedAt", "UpdatedBy"]),
      IBookingRepository
{
    private const string BookingColumns =
        "b.Id, b.CustomerId, b.SessionId, b.Status, b.MovieTitle, b.SessionStartsAt, b.TotalAmount, b.CancelReason, " +
        "b.CreatedAt, b.CreatedBy, b.UpdatedAt, b.UpdatedBy, b.IsDeleted, b.RowVersion";

    protected override string SelectColumns =>
        "Id, CustomerId, SessionId, Status, MovieTitle, SessionStartsAt, TotalAmount, CancelReason, " +
        "CreatedAt, CreatedBy, UpdatedAt, UpdatedBy, IsDeleted, RowVersion";

    protected override string? ActiveFilter => "IsDeleted = 0";

    /// <summary>
    /// Два набори результатів одним зверненням (QueryMultipleAsync):
    /// 1) бронювання + квитки — multi-mapping рядків JOIN; 2) оплата (1:1).
    /// </summary>
    public Task<Domain.Booking?> GetWithDetailsAsync(long id, CancellationToken ct = default) =>
        SqlErrorTranslator.Run(async () =>
        {
            await EnsureOpenAsync(ct);
            await using var grid = await Connection.QueryMultipleAsync(Cmd($"""
                SELECT {BookingColumns},
                       t.BookingId, t.SeatId, t.SessionId, t.Price, t.IsCancelled, s.RowNumber, s.SeatNumber, s.SeatType
                FROM dbo.Bookings b
                LEFT JOIN dbo.Tickets t ON t.BookingId = b.Id
                LEFT JOIN dbo.Seats s   ON s.Id = t.SeatId
                WHERE b.Id = @Id AND b.IsDeleted = 0
                ORDER BY s.RowNumber, s.SeatNumber;

                SELECT BookingId, Amount, Method, TransactionId, PaidAt
                FROM dbo.BookingPayments WHERE BookingId = @Id;
                """, new { Id = id }, ct));

            Domain.Booking? result = null;
            grid.Read<Domain.Booking, Ticket?, Domain.Booking>((booking, ticket) =>
            {
                result ??= booking;                       // кожен рядок JOIN: бронювання + один квиток
                if (ticket is not null) result.Tickets.Add(ticket);
                return result;
            }, splitOn: "BookingId");                     // з цього стовпця починається Ticket

            var payment = await grid.ReadSingleOrDefaultAsync<BookingPayment>();
            if (result is not null) result.Payment = payment;
            return result;
        });

    public Task<Domain.Booking?> GetForUpdateAsync(long id, CancellationToken ct = default) =>
        SqlErrorTranslator.Run(async () =>
        {
            await EnsureOpenAsync(ct);
            return await Connection.QuerySingleOrDefaultAsync<Domain.Booking>(Cmd($"""
                SELECT {BookingColumns}
                FROM dbo.Bookings b WITH (UPDLOCK, ROWLOCK)
                WHERE b.Id = @Id AND b.IsDeleted = 0
                """, new { Id = id }, ct));
        });

    public Task<PagedResult<Domain.Booking>> GetByCustomerAsync(long customerId, int page, int pageSize, CancellationToken ct = default) =>
        SqlErrorTranslator.Run(async () =>
        {
            await EnsureOpenAsync(ct);
            await using var grid = await Connection.QueryMultipleAsync(Cmd($"""
                SELECT COUNT(*) FROM dbo.Bookings WHERE CustomerId = @CustomerId AND IsDeleted = 0;

                SELECT {BookingColumns}
                FROM dbo.Bookings b
                WHERE b.CustomerId = @CustomerId AND b.IsDeleted = 0
                ORDER BY b.CreatedAt DESC, b.Id DESC
                OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
                """, new { CustomerId = customerId, Skip = (page - 1) * pageSize, Take = pageSize }, ct));

            var total = await grid.ReadSingleAsync<int>();
            var items = (await grid.ReadAsync<Domain.Booking>()).AsList();
            return new PagedResult<Domain.Booking>(items, page, pageSize, total);
        });

    /// <summary>Вставка кількох квитків: Dapper виконує команду для кожного елемента колекції.</summary>
    public Task AddTicketsAsync(IEnumerable<Ticket> tickets, CancellationToken ct = default) =>
        SqlErrorTranslator.Run(async () =>
        {
            await EnsureOpenAsync(ct);
            await Connection.ExecuteAsync(Cmd("""
                INSERT INTO dbo.Tickets (BookingId, SeatId, SessionId, Price)
                VALUES (@BookingId, @SeatId, @SessionId, @Price)
                """, tickets, ct));
        });

    /// <summary>
    /// Зайняті місця сеансу. UPDLOCK + HOLDLOCK тримають блокування діапазону до кінця транзакції,
    /// тож паралельне бронювання тих самих місць чекатиме (додатково страхує фільтрований UNIQUE-індекс).
    /// </summary>
    public Task<IReadOnlyList<int>> GetTakenSeatIdsAsync(long sessionId, IReadOnlyCollection<int> seatIds, CancellationToken ct = default) =>
        SqlErrorTranslator.Run<IReadOnlyList<int>>(async () =>
        {
            await EnsureOpenAsync(ct);
            var rows = await Connection.QueryAsync<int>(Cmd("""
                SELECT SeatId FROM dbo.Tickets WITH (UPDLOCK, HOLDLOCK)
                WHERE SessionId = @SessionId AND SeatId IN @SeatIds AND IsCancelled = 0
                """, new { SessionId = sessionId, SeatIds = seatIds }, ct));
            return rows.AsList();
        });

    public Task<bool> MarkPaidAsync(long id, string? userName, CancellationToken ct = default) =>
        SqlErrorTranslator.Run(async () =>
        {
            await EnsureOpenAsync(ct);
            return await Connection.ExecuteAsync(Cmd("""
                UPDATE dbo.Bookings
                SET Status = 'Paid', UpdatedAt = SYSUTCDATETIME(), UpdatedBy = @UserName
                WHERE Id = @Id AND Status = 'Created' AND IsDeleted = 0
                """, new { Id = id, UserName = userName }, ct)) == 1;
        });

    /// <summary>Виклик процедури з Лаб. 01: граф переходів і звільнення місць перевіряє/виконує БД.</summary>
    public Task ChangeStatusAsync(long id, string newStatus, string? reason, byte[]? expectedRowVersion,
        string? userName, CancellationToken ct = default) =>
        SqlErrorTranslator.Run(async () =>
        {
            await EnsureOpenAsync(ct);
            var parameters = new DynamicParameters();
            parameters.Add("@BookingId", id, DbType.Int64);
            parameters.Add("@NewStatus", newStatus, DbType.AnsiString, size: 20);
            parameters.Add("@Reason", reason, DbType.String, size: 300);
            parameters.Add("@ExpectedRowVersion", expectedRowVersion, DbType.Binary, size: 8);
            parameters.Add("@UserName", userName, DbType.String, size: 100);
            await Connection.ExecuteAsync(Cmd("dbo.usp_Booking_ChangeStatus", parameters, ct, CommandType.StoredProcedure));
        });

    public Task<bool> HasActiveBookingsAsync(long customerId, CancellationToken ct = default) =>
        SqlErrorTranslator.Run(async () =>
        {
            await EnsureOpenAsync(ct);
            return await Connection.ExecuteScalarAsync<int>(Cmd("""
                SELECT COUNT(1) FROM dbo.Bookings
                WHERE CustomerId = @CustomerId AND IsDeleted = 0
                  AND Status IN ('Created', 'Paid') AND SessionStartsAt > SYSUTCDATETIME()
                """, new { CustomerId = customerId }, ct)) > 0;
        });
}
