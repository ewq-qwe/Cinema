using System.Diagnostics;

namespace Booking.Bll.Diagnostics;

/// <summary>
/// Власні spans бізнес-операцій бронювання (Лаб. 05). Джерело підключено до OpenTelemetry в ServiceDefaults,
/// тож span «booking.create» тощо видно в trace між HTTP-запитом і SQL-командами.
/// </summary>
public static class BookingTelemetry
{
    public const string SourceName = "Booking.Bll";

    public static readonly ActivitySource Source = new(SourceName);
}
