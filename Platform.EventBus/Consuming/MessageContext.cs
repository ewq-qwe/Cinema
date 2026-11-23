namespace Platform.EventBus.Consuming;

/// <summary>
/// Метадані повідомлення, що обробляється (Scoped — один на повідомлення). Обробник може взяти з нього
/// CorrelationId / номер спроби і позначити повідомлення як дублікат (для логів і метрики messaging.consumed{result=duplicate}).
/// </summary>
public sealed class MessageContext
{
    public Guid EventId { get; internal set; }
    public string EventType { get; internal set; } = "";
    public string Queue { get; internal set; } = "";
    public string? CorrelationId { get; internal set; }

    /// <summary>Номер повторної спроби (0 — перша доставка).</summary>
    public int RetryCount { get; internal set; }

    /// <summary>Брокер позначив повідомлення як повторно доставлене (з'єднання обірвалося до ack).</summary>
    public bool Redelivered { get; internal set; }

    public bool IsDuplicate { get; private set; }

    /// <summary>Подію з цим EventId уже оброблено — повторна обробка пропущена.</summary>
    public void MarkDuplicate() => IsDuplicate = true;
}
