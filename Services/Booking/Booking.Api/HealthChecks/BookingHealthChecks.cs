using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Booking.Api.HealthChecks;

public static class BookingHealthChecks
{
    /// <summary>
    /// Readiness сервісу бронювання: SQL Server (CinemaBooking). Легка перевірка — <c>SELECT 1</c>, таймаут 5 с;
    /// без БД сервіс не може обслуговувати жодного запиту, тож збій → Unhealthy (виключення з балансування).
    /// Liveness (<c>self</c>, тег live) додає ServiceDefaults — він не залежить від БД.
    /// </summary>
    public static IServiceCollection AddBookingHealthChecks(this IServiceCollection services, IConfiguration config)
    {
        services.AddHealthChecks()
            .AddSqlServer(
                connectionString: config.GetConnectionString("BookingDb")!,
                healthQuery: "SELECT 1;",
                name: "booking-db",
                failureStatus: HealthStatus.Unhealthy,
                tags: [HealthTags.Database, "sql", HealthTags.Ready],
                timeout: TimeSpan.FromSeconds(5));
        return services;
    }
}
