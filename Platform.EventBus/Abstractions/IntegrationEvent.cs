namespace Platform.EventBus.Abstractions;

/// <summary>
/// Базовий тип усіх integration events платформи (як IntegrationEvent в eShop).
/// Record — незмінний (лише init) і з порівнянням за значенням; подія описує бізнес-факт у минулому часі
/// і містить УСІ дані для обробки — споживач не звертається до сервісу-власника по додаткові дані.
/// </summary>
public abstract record IntegrationEvent
{
    /// <summary>Унікальний ідентифікатор події: MessageId у RabbitMQ і ключ ідемпотентності в споживачах.</summary>
    public Guid EventId { get; init; } = Guid.NewGuid();

    /// <summary>Час бізнес-факту (UTC). Споживачі за ним відкидають застарілі події, що прийшли пізніше (out-of-order).</summary>
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;

    /// <summary>Наскрізний CorrelationId (Лаб. 05) запиту, що спричинив подію — для бізнес-трасування в логах.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// Версія схеми події. Правило еволюції: нові поля лише ДОДАЮТЬСЯ (старий споживач їх ігнорує),
    /// наявні не перейменовуються й не видаляються; несумісна зміна — нова версія / новий тип події.
    /// </summary>
    public int SchemaVersion { get; init; } = 1;
}
