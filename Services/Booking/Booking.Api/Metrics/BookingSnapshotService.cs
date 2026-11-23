using Booking.Bll.Services;

namespace Booking.Api.Metrics;

/// <summary>
/// Оновлює знімок для gauge sessions.upcoming раз на 30 с. Callback gauge виконується в момент збору метрик
/// і не повинен ходити в БД — тому значення готується заздалегідь. Збій БД лише логується (знімок лишається старим).
/// </summary>
public sealed class BookingSnapshotService(IServiceScopeFactory scopes, BookingMetrics metrics, ILogger<BookingSnapshotService> logger)
    : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var upcoming = await scope.ServiceProvider.GetRequiredService<ISessionService>().GetUpcomingAsync(null, stoppingToken);
                metrics.SetUpcomingSessions(upcoming.Count);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning("Знімок для метрики sessions.upcoming не оновлено: {Reason}", ex.Message);
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
