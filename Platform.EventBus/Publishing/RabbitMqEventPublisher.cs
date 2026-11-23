using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Platform.EventBus.Abstractions;
using Platform.EventBus.Diagnostics;
using Platform.EventBus.Serialization;
using Platform.EventBus.Topology;
using RabbitMQ.Client;

namespace Platform.EventBus.Publishing;

/// <summary>
/// Публікація integration events у RabbitMQ. IConnection — один на застосунок (реєструє AddRabbitMQClient),
/// канал (IChannel) — окремий на кожну публікацію. Канал відкривається з publisher confirms: BasicPublishAsync
/// завершується лише тоді, коли брокер підтвердив прийняття повідомлення в durable-чергу (інакше — виняток,
/// і подія лишається неопублікованою в outbox, звідки її повторить фоновий процес).
/// </summary>
public sealed class RabbitMqEventPublisher(
    IConnection connection,
    MessagingMetrics metrics,
    ILogger<RabbitMqEventPublisher> logger,
    string? sourceName = null) : IEventPublisher
{
    private static readonly CreateChannelOptions ConfirmedChannel =
        new(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true);

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _declaredExchanges = new();
    private readonly SemaphoreSlim _declareLock = new(1, 1);

    public Task PublishAsync<T>(T @event, string exchange, string routingKey, CancellationToken ct = default)
        where T : IntegrationEvent =>
        PublishAsync(new EventEnvelope(@event.EventId, @event.GetType().Name, exchange, routingKey, EventJson.Serialize(@event),
            @event.OccurredAt, @event.CorrelationId, @event.SchemaVersion), ct);

    public async Task PublishAsync(EventEnvelope envelope, CancellationToken ct = default)
    {
        // У межах HTTP-запиту span публікації — нащадок поточного; з outbox (фоновий процес) — нащадок збереженого traceparent
        using var activity = TracePropagation.StartPublishActivity(envelope.Exchange, envelope.RoutingKey, envelope.EventType,
            envelope.EventId, Activity.Current is null ? envelope.TraceParent : null);
        activity?.SetTag("correlation.id", envelope.CorrelationId);

        var props = new BasicProperties
        {
            Persistent = true,                                     // DeliveryMode = 2: повідомлення переживе перезапуск брокера
            ContentType = EventJson.ContentType,
            ContentEncoding = "utf-8",
            MessageId = envelope.EventId.ToString(),               // ключ ідемпотентності в споживачах
            Type = envelope.EventType,                             // за ним споживач вибирає клас і обробник
            CorrelationId = envelope.CorrelationId,
            Timestamp = new AmqpTimestamp(new DateTimeOffset(DateTime.SpecifyKind(envelope.OccurredAt, DateTimeKind.Utc)).ToUnixTimeSeconds()),
            AppId = sourceName,
            Headers = new Dictionary<string, object?>
            {
                [MessageHeaders.CorrelationId] = envelope.CorrelationId,
                [MessageHeaders.SchemaVersion] = envelope.SchemaVersion,
                [MessageHeaders.OccurredAt] = envelope.OccurredAt.ToString("O"),
                [MessageHeaders.Source] = sourceName
            }
        };
        TracePropagation.Inject(props);   // traceparent / tracestate / baggage

        try
        {
            await using var channel = await connection.CreateChannelAsync(ConfirmedChannel, ct);
            await EnsureExchangeAsync(channel, envelope.Exchange, ct);
            await channel.BasicPublishAsync(envelope.Exchange, envelope.RoutingKey, mandatory: false, props, envelope.Body, ct);
        }
        catch (Exception ex)
        {
            metrics.Published(envelope.EventType, success: false);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            logger.LogError(ex, "Publish failed {EventType} {EventId} to {Exchange}/{RoutingKey}, CorrelationId {CorrelationId}",
                envelope.EventType, envelope.EventId, envelope.Exchange, envelope.RoutingKey, envelope.CorrelationId);
            throw;
        }

        metrics.Published(envelope.EventType, success: true);
        logger.LogInformation("Published {EventType} {EventId} to {Exchange}/{RoutingKey}, CorrelationId {CorrelationId}",
            envelope.EventType, envelope.EventId, envelope.Exchange, envelope.RoutingKey, envelope.CorrelationId);
    }

    /// <summary>Exchange оголошується один раз на процес (повторне оголошення безпечне, але зайве).</summary>
    private async Task EnsureExchangeAsync(IChannel channel, string exchange, CancellationToken ct)
    {
        if (_declaredExchanges.ContainsKey(exchange)) return;
        await _declareLock.WaitAsync(ct);
        try
        {
            if (_declaredExchanges.ContainsKey(exchange)) return;
            await EventBusTopology.DeclareExchangeAsync(channel, exchange, ct);
            _declaredExchanges[exchange] = true;
        }
        finally
        {
            _declareLock.Release();
        }
    }
}
