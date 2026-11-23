namespace Catalog.Dal.IntegrationEventLog;

/// <summary>Стан запису outbox.</summary>
public enum EventState
{
    /// <summary>Збережено разом зі змінами, ще не підтверджено брокером.</summary>
    NotPublished = 0,

    /// <summary>Брокер підтвердив прийняття (publisher confirm).</summary>
    Published = 1
}

/// <summary>
/// Лаб. 08 — Transactional Outbox (як IntegrationEventLogEF в eShop). Подія записується в таблицю IntegrationEventLog
/// ТІЄЮ САМОЮ транзакцією SaveChanges, що й зміни фільму: або збережено обидва, або нічого.
/// Публікація — після коміту; якщо застосунок упав між комітом і публікацією або брокер був недоступний,
/// запис лишається NotPublished і його опублікує фоновий OutboxPublisherService (подія не губиться).
/// </summary>
public class IntegrationEventLogEntry
{
    public const int TypeMaxLength = 100;
    public const int ErrorMaxLength = 1000;

    private IntegrationEventLogEntry() { } // для EF Core

    public IntegrationEventLogEntry(Guid eventId, string eventType, string exchange, string routingKey, string content,
        DateTime occurredAt, string? correlationId, int schemaVersion, string? traceParent)
    {
        EventId = eventId;
        EventType = eventType;
        Exchange = exchange;
        RoutingKey = routingKey;
        Content = content;
        OccurredAt = occurredAt;
        CorrelationId = correlationId;
        SchemaVersion = schemaVersion;
        TraceParent = traceParent;
        State = EventState.NotPublished;
    }

    /// <summary>EventId події = первинний ключ (і MessageId у RabbitMQ).</summary>
    public Guid EventId { get; private set; }
    public string EventType { get; private set; } = "";
    public string Exchange { get; private set; } = "";
    public string RoutingKey { get; private set; } = "";

    /// <summary>Тіло події — той самий JSON, що піде в брокер.</summary>
    public string Content { get; private set; } = "";
    public DateTime OccurredAt { get; private set; }
    public string? CorrelationId { get; private set; }
    public int SchemaVersion { get; private set; }

    /// <summary>W3C traceparent операції, що створила подію: публікація з фонового процесу лишається в тому самому trace.</summary>
    public string? TraceParent { get; private set; }

    public EventState State { get; private set; }
    public int TimesSent { get; private set; }
    public DateTime? PublishedAt { get; private set; }
    public string? LastError { get; private set; }

    public void MarkPublished()
    {
        TimesSent++;
        State = EventState.Published;
        PublishedAt = DateTime.UtcNow;
        LastError = null;
    }

    public void MarkFailed(string error)
    {
        TimesSent++;
        LastError = error.Length <= ErrorMaxLength ? error : error[..ErrorMaxLength];
    }
}
