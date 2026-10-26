using Booking.Dal.Abstractions;
using Booking.Domain;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Booking.Dal.Repositories;

/// <summary>Оплати бронювань на Dapper. Ключ — BookingId (1:1 з Bookings).</summary>
public sealed class PaymentRepository(SqlConnection connection, Func<SqlTransaction?> currentTransaction)
    : BaseDapperRepository<BookingPayment>(connection, currentTransaction,
        table: "dbo.BookingPayments", key: "BookingId",
        insertColumns: ["BookingId", "Amount", "Method", "TransactionId"],
        updateColumns: ["Amount", "Method"]),
      IPaymentRepository
{
    protected override string SelectColumns => "BookingId, Amount, Method, TransactionId, PaidAt";

    public Task<BookingPayment?> GetByTransactionIdAsync(string transactionId, CancellationToken ct = default) =>
        SqlErrorTranslator.Run(async () =>
        {
            await EnsureOpenAsync(ct);
            return await Connection.QuerySingleOrDefaultAsync<BookingPayment>(Cmd(
                $"SELECT {SelectColumns} FROM dbo.BookingPayments WHERE TransactionId = @TransactionId",
                new { TransactionId = transactionId }, ct));
        });
}
