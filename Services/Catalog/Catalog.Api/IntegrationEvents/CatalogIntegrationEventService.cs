using System.Diagnostics;
using System.Text;
using Catalog.Bll.IntegrationEvents;
using Catalog.Dal.Abstractions;
using Catalog.Dal.IntegrationEventLog;
using Platform.EventBus;
using Platform.EventBus.Abstractions;
using Platform.EventBus.Events;
using Platform.EventBus.Serialization;

namespace Catalog.Api.IntegrationEvents;

/// <summary>
/// Лаб. 08: transactional outbox Каталогу. Scoped — той самий Unit of Work (DbContext), що й у MovieService,
/// тому запис IntegrationEventLog зберігається тим самим SaveChangesAsync, що й зміни фільму.
/// </summary>
public sealed class CatalogIntegrationEventService(
    IUnitOfWork uow,
    IEventPublisher publisher,
    ICorrelationIdProvider correlation,
    ILogger<CatalogIntegrationEventService> logger) : ICatalogIntegrationEventService
{
    private readonly List<IntegrationEventLogEntry> _pending = [];

    public void AddEvent(IntegrationEvent @event, string routingKey)
    {
        // Record незмінний: CorrelationId підставляємо через with (копія з тим самим EventId і OccurredAt)
        @event = @event with { CorrelationId = @event.CorrelationId ?? correlation.Current };
        var entry = new IntegrationEventLogEntry(@event.EventId, @event.GetType().Name, CatalogEventRoutes.Exchange, routingKey,
            Encoding.UTF8.GetString(EventJson.Serialize(@event)), @event.OccurredAt, @event.CorrelationId, @event.SchemaVersion,
            Activity.Current?.Id);   // W3C traceparent поточної операції
        uow.IntegrationEventLog.Add(entry);
        _pending.Add(entry);
        logger.LogInformation("Outbox: {EventType} {EventId} for movie event {RoutingKey}, CorrelationId {CorrelationId} — збережеться разом зі змінами",
            entry.EventType, entry.EventId, routingKey, entry.CorrelationId);
    }

    public async Task PublishPendingAsync(CancellationToken ct = default)
    {
        if (_pending.Count == 0) return;
        foreach (var entry in _pending)
            await OutboxPublishing.PublishAsync(entry, publisher, logger, ct);
        _pending.Clear();
        // Стан Published / LastError — окремим SaveChanges: бізнес-зміни вже зафіксовано, тут лише облік outbox
        await uow.SaveChangesAsync(CancellationToken.None);
    }
}

/// <summary>Публікація одного запису outbox (спільна для запиту й фонового процесу).</summary>
internal static class OutboxPublishing
{
    public static async Task<bool> PublishAsync(IntegrationEventLogEntry entry, IEventPublisher publisher, ILogger logger, CancellationToken ct)
    {
        try
        {
            await publisher.PublishAsync(new EventEnvelope(entry.EventId, entry.EventType, entry.Exchange, entry.RoutingKey,
                Encoding.UTF8.GetBytes(entry.Content), entry.OccurredAt, entry.CorrelationId, entry.SchemaVersion, entry.TraceParent), ct);
            entry.MarkPublished();
            return true;
        }
        catch (Exception ex)
        {
            // Брокер недоступний — бізнес-операцію НЕ скасовуємо: подія лишається NotPublished і піде з фонового процесу
            entry.MarkFailed($"{ex.GetType().Name}: {ex.Message}");
            logger.LogWarning(ex, "Outbox: {EventType} {EventId} не опубліковано (спроба {TimesSent}), повторить OutboxPublisherService; CorrelationId {CorrelationId}",
                entry.EventType, entry.EventId, entry.TimesSent, entry.CorrelationId);
            return false;
        }
    }
}
