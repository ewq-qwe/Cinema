namespace Platform.EventBus.Abstractions;

/// <summary>Імена заголовків повідомлень — однакові для publisher і consumer.</summary>
public static class MessageHeaders
{
    /// <summary>Наскрізний CorrelationId (бізнес-трасування, Лаб. 05).</summary>
    public const string CorrelationId = "x-correlation-id";

    /// <summary>Версія схеми події.</summary>
    public const string SchemaVersion = "x-schema-version";

    /// <summary>Час бізнес-факту (ISO 8601, UTC).</summary>
    public const string OccurredAt = "x-occurred-at";

    /// <summary>Скільки разів повідомлення вже повторно опубліковано після тимчасового збою.</summary>
    public const string RetryCount = "x-retry-count";

    /// <summary>Причина останнього збою (для аналізу повідомлень у DLQ).</summary>
    public const string LastError = "x-last-error";

    /// <summary>Сервіс-публікатор.</summary>
    public const string Source = "x-source";

    /// <summary>Заголовок, який RabbitMQ додає при dead-lettering: причина, черга, кількість.</summary>
    public const string Death = "x-death";
}
