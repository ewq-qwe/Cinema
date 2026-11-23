namespace Platform.EventBus.Abstractions;

/// <summary>Публікація integration events у брокер (реалізація — RabbitMqEventPublisher).</summary>
public interface IEventPublisher
{
    /// <summary>
    /// Серіалізує подію в JSON і публікує її в <paramref name="exchange"/> з <paramref name="routingKey"/>.
    /// Викликати лише ПІСЛЯ успішного збереження змін у БД.
    /// </summary>
    Task PublishAsync<T>(T @event, string exchange, string routingKey, CancellationToken ct = default) where T : IntegrationEvent;

    /// <summary>
    /// Публікує вже серіалізовану подію (наприклад, із transactional outbox): тіло, тип і контекст трасування
    /// беруться з конверта, тож повторна публікація з outbox дає те саме повідомлення з тим самим EventId.
    /// </summary>
    Task PublishAsync(EventEnvelope envelope, CancellationToken ct = default);
}

/// <summary>Обробник одного типу події. Реєструється Scoped: свій екземпляр (і свої DbContext / UoW) на кожне повідомлення.</summary>
public interface IIntegrationEventHandler<in TEvent> where TEvent : IntegrationEvent
{
    Task HandleAsync(TEvent @event, CancellationToken ct);
}

/// <summary>
/// Серіалізована подія з метаданими — одиниця публікації. Так її зберігає outbox і так вона йде в RabbitMQ.
/// </summary>
/// <param name="EventId">MessageId повідомлення.</param>
/// <param name="EventType">Ім'я типу події (BasicProperties.Type) — за ним споживач вибирає клас і обробник.</param>
/// <param name="Exchange">Exchange призначення.</param>
/// <param name="RoutingKey">Ключ маршрутизації, напр. movie.renamed.</param>
/// <param name="Body">JSON (camelCase, UTF-8).</param>
/// <param name="OccurredAt">Час бізнес-факту.</param>
/// <param name="CorrelationId">CorrelationId запиту-джерела.</param>
/// <param name="SchemaVersion">Версія схеми події.</param>
/// <param name="TraceParent">W3C traceparent операції, що створила подію (для публікації з outbox поза HTTP-запитом).</param>
public sealed record EventEnvelope(
    Guid EventId,
    string EventType,
    string Exchange,
    string RoutingKey,
    ReadOnlyMemory<byte> Body,
    DateTime OccurredAt,
    string? CorrelationId,
    int SchemaVersion = 1,
    string? TraceParent = null);

/// <summary>
/// Постійний збій обробки: повтор не допоможе (некоректні або несумісні дані, порушення бізнес-правила).
/// Повідомлення одразу йде в DLQ, без повторів.
/// </summary>
public sealed class NonRetryableEventException(string message, Exception? inner = null) : Exception(message, inner);
