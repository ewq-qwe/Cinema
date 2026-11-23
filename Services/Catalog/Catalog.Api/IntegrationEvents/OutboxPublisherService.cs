using Catalog.Dal.Abstractions;
using Platform.EventBus.Abstractions;

namespace Catalog.Api.IntegrationEvents;

/// <summary>
/// Лаб. 08: фоновий процес transactional outbox. Раз на <see cref="Interval"/> публікує події, що лишилися NotPublished:
/// застосунок упав між комітом і публікацією або брокер був недоступний. Події, молодші за <see cref="Grace"/>,
/// не чіпає — їх зараз публікує сам запит. Порядок — за часом виникнення; EventId той самий, тож повторна
/// доставка безпечна (споживачі ідемпотентні).
/// </summary>
public sealed class OutboxPublisherService(IServiceScopeFactory scopes, IEventPublisher publisher, ILogger<OutboxPublisherService> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(10);
    private const int BatchSize = 50;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await PublishBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Outbox: прохід не вдався (БД недоступна?), наступний через {IntervalSeconds} с", Interval.TotalSeconds);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    private async Task PublishBatchAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var pending = await uow.IntegrationEventLog.GetPendingAsync(DateTime.UtcNow - Grace, BatchSize, ct);
        if (pending.Count == 0) return;

        logger.LogInformation("Outbox: {PendingCount} неопублікованих подій", pending.Count);
        var published = 0;
        foreach (var entry in pending)
        {
            if (!await OutboxPublishing.PublishAsync(entry, publisher, logger, ct))
                break;   // брокер недоступний — решту спробуємо в наступному проході, зберігши порядок
            published++;
        }
        await uow.SaveChangesAsync(CancellationToken.None);
        logger.LogInformation("Outbox: опубліковано {PublishedCount} з {PendingCount}", published, pending.Count);
    }
}
