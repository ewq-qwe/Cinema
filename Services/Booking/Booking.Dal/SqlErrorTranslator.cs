using Booking.Domain.Exceptions;
using Microsoft.Data.SqlClient;

namespace Booking.Dal;

/// <summary>
/// Перетворює SqlException на доменні винятки, щоб сирі SQL-повідомлення не потрапляли в API.
/// Номери 500xx — керовані помилки процедур з Лаб. 01 (db/p1/procedures.sql).
/// </summary>
internal static class SqlErrorTranslator
{
    public static Exception Translate(SqlException ex) => ex.Number switch
    {
        50001 => new NotFoundException(ex.Message, ex),                     // не знайдено
        50002 or 50003 or 50004 or 50005 or 50006 or 50007
              => new BusinessConflictException(ex.Message, ex),             // недопустимий перехід, сеанс почався, місце зайняте…
        2601 or 2627 => new BusinessConflictException("Запис з таким унікальним значенням уже існує.", ex),
        547 => new BusinessConflictException("Операцію відхилено: на запис посилаються інші дані або посилання некоректне.", ex),
        _ => ex
    };

    public static async Task<T> Run<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (SqlException ex)
        {
            var translated = Translate(ex);
            if (ReferenceEquals(translated, ex)) throw;
            throw translated;
        }
    }

    public static Task Run(Func<Task> action) => Run(async () => { await action(); return 0; });
}
