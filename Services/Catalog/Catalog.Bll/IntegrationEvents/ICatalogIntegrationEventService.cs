using Platform.EventBus.Abstractions;

namespace Catalog.Bll.IntegrationEvents;

/// <summary>
/// Лаб. 08: публікація integration events Каталогу через transactional outbox (як ICatalogIntegrationEventService в eShop).
/// Порядок у бізнес-операції:
/// <list type="number">
/// <item><see cref="AddEvent"/> — подія потрапляє в outbox (IntegrationEventLog) того самого Unit of Work;</item>
/// <item><c>uow.SaveChangesAsync()</c> — зміни сутності й запис outbox фіксуються ОДНІЄЮ транзакцією;</item>
/// <item><see cref="PublishPendingAsync"/> — лише після успішного збереження подія йде в брокер.</item>
/// </list>
/// Якщо збереження не вдалося — винятком перериває операцію, і подія не публікується (в outbox її теж немає).
/// </summary>
public interface ICatalogIntegrationEventService
{
    /// <summary>Додати подію в outbox (без збереження); CorrelationId поточного запиту підставляється автоматично.</summary>
    void AddEvent(IntegrationEvent @event, string routingKey);

    /// <summary>Опублікувати події, додані в цьому scope. Збій брокера не скасовує вже збережену бізнес-операцію:
    /// подія лишається в outbox і її опублікує фоновий процес.</summary>
    Task PublishPendingAsync(CancellationToken ct = default);
}
