using System.Diagnostics;
using System.Text;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Platform.EventBus.Diagnostics;

/// <summary>
/// Передача контексту трасування через брокер. Publisher записує W3C traceparent (і baggage) у заголовки повідомлення,
/// consumer відновлює його й починає Activity типу Consumer як нащадка — так утворюється наскрізний trace
/// HTTP-запит → публікація → обробка в іншому сервісі → БД. Механізм реалізовано вручну, щоб було видно,
/// як контекст переходить через брокер (вбудоване трасування RabbitMQ.Client вимкнено в AddRabbitMQClient).
/// ActivitySource "Platform.Messaging" підключено в ServiceDefaults (.AddSource).
/// </summary>
public static class TracePropagation
{
    public const string SourceName = "Platform.Messaging";
    public static readonly ActivitySource Source = new(SourceName);

    // Стандартні W3C Trace Context (traceparent / tracestate) і Baggage — те саме, що Propagators.DefaultTextMapPropagator
    // після ініціалізації OpenTelemetry SDK, але без залежності від порядку ініціалізації (до неї там Noop-пропагатор)
    private static readonly TextMapPropagator Propagator =
        new CompositeTextMapPropagator([new TraceContextPropagator(), new BaggagePropagator()]);

    /// <summary>Span публікації (Producer). Якщо задано <paramref name="parentTraceParent"/> (публікація з outbox) — його нащадок.</summary>
    public static Activity? StartPublishActivity(string exchange, string routingKey, string eventType, Guid eventId, string? parentTraceParent = null)
    {
        var parent = parentTraceParent is not null && ActivityContext.TryParse(parentTraceParent, null, out var ctx) ? ctx : default;
        var activity = parent == default
            ? Source.StartActivity($"{exchange} publish", ActivityKind.Producer)
            : Source.StartActivity($"{exchange} publish", ActivityKind.Producer, parent);
        if (activity is null) return null;
        activity.SetTag("messaging.system", "rabbitmq");
        activity.SetTag("messaging.operation.type", "send");
        activity.SetTag("messaging.destination.name", exchange);
        activity.SetTag("messaging.rabbitmq.destination.routing_key", routingKey);
        activity.SetTag("messaging.message.id", eventId.ToString());
        activity.SetTag("event.type", eventType);
        return activity;
    }

    /// <summary>Записує контекст поточного Activity у заголовки (traceparent, tracestate, baggage).</summary>
    public static void Inject(BasicProperties props)
    {
        if (Activity.Current is not { } activity) return;
        props.Headers ??= new Dictionary<string, object?>();
        Propagator.Inject(new PropagationContext(activity.Context, Baggage.Current), props.Headers,
            static (headers, key, value) => headers[key] = value);
    }

    /// <summary>
    /// Відновлює контекст із заголовків отриманого повідомлення (значення приходять як byte[]) і починає Activity типу Consumer.
    /// </summary>
    public static Activity? StartConsumerActivity(BasicDeliverEventArgs ea, string queue)
    {
        var parent = Propagator.Extract(default, ea.BasicProperties.Headers, ReadHeader);
        Baggage.Current = parent.Baggage;
        var activity = Source.StartActivity($"{queue} process", ActivityKind.Consumer, parent.ActivityContext);
        if (activity is null) return null;
        activity.SetTag("messaging.system", "rabbitmq");
        activity.SetTag("messaging.operation.type", "process");
        activity.SetTag("messaging.destination.name", queue);
        activity.SetTag("messaging.rabbitmq.destination.routing_key", ea.RoutingKey);
        activity.SetTag("messaging.message.id", ea.BasicProperties.MessageId);
        activity.SetTag("messaging.rabbitmq.message.delivery_tag", (long)ea.DeliveryTag);
        activity.SetTag("event.type", ea.BasicProperties.Type);
        return activity;
    }

    private static IEnumerable<string> ReadHeader(IDictionary<string, object?>? headers, string key) =>
        headers is not null && headers.TryGetValue(key, out var value) && HeaderValue(value) is { } s ? [s] : [];

    /// <summary>Рядкові заголовки RabbitMQ повертає як byte[] (AMQP long string).</summary>
    public static string? HeaderValue(object? value) => value switch
    {
        byte[] bytes => Encoding.UTF8.GetString(bytes),
        string s => s,
        null => null,
        _ => value.ToString()
    };
}
