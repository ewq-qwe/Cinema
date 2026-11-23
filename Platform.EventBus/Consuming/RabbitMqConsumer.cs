using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Platform.EventBus.Abstractions;
using Platform.EventBus.Diagnostics;
using Platform.EventBus.Serialization;
using Platform.EventBus.Topology;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Platform.EventBus.Consuming;

/// <summary>
/// Базовий довготривалий споживач (BackgroundService): власна черга сервісу, ручне підтвердження, повтори й DLQ.
/// Похідний клас у сервісі задає підписку (<see cref="Subscription"/>) і типи подій (<see cref="Handle{TEvent}"/>);
/// бізнес-обробка — в окремих IIntegrationEventHandler&lt;T&gt;, що створюються в scope КОЖНОГО повідомлення.
/// </summary>
/// <remarks>
/// Рішення щодо повідомлення:
/// <list type="bullet">
/// <item>успіх (або дублікат) → <c>BasicAckAsync</c>: повідомлення видаляється з черги;</item>
/// <item>постійний збій (некоректний JSON, невідомий тип, <see cref="NonRetryableEventException"/>) →
///       <c>BasicNackAsync(requeue: false)</c> → dead letter exchange → DLQ, без повторів;</item>
/// <item>тимчасовий збій (БД недоступна, таймаут) → копія з <c>x-retry-count + 1</c> публікується в ту саму чергу,
///       оригінал підтверджується; після <see cref="MaxRetries"/> спроб — DLQ.</item>
/// </list>
/// Лічильник не можна вести через <c>BasicNack(requeue: true)</c>: повернене повідомлення приходить знову
/// без змін (заголовки змінити не можна) і повторювалося б нескінченно й без затримки.
/// </remarks>
public abstract class RabbitMqConsumer : BackgroundService
{
    private static readonly CreateChannelOptions ConfirmedChannel =
        new(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true);

    private readonly IConnection _connection;
    private readonly IServiceScopeFactory _scopes;
    private readonly MessagingMetrics _metrics;
    private readonly ILogger _logger;
    private readonly Dictionary<string, EventRoute> _routes = new(StringComparer.Ordinal);

    private IChannel? _channel;        // споживання + ack / nack
    private IChannel? _retryChannel;   // повторна публікація з publisher confirms
    private string? _consumerTag;
    private int _inFlight;
    private CancellationToken _stopping;

    protected RabbitMqConsumer(IConnection connection, IServiceScopeFactory scopes, MessagingMetrics metrics, ILogger logger)
    {
        _connection = connection;
        _scopes = scopes;
        _metrics = metrics;
        _logger = logger;
    }

    /// <summary>Черга сервісу, exchange-джерело й bindings.</summary>
    protected abstract QueueSubscription Subscription { get; }

    /// <summary>Скільки непідтверджених повідомлень брокер може видати споживачу одночасно (BasicQos).</summary>
    protected virtual ushort PrefetchCount => 10;

    /// <summary>Максимум повторних спроб після тимчасового збою; далі — DLQ.</summary>
    protected virtual int MaxRetries => 3;

    /// <summary>
    /// Затримка перед спробою N: 2 с, 10 с, 30 с (разом ~40 с). Тимчасовому збою треба дати час минути: перезапуск SQL Server
    /// триває 10–20 с, а SqlClient після невдалого з'єднання ще кілька секунд одразу відхиляє нові (blocking period пулу),
    /// тож короткі затримки 1/2/4 с вичерпали б усі спроби до того, як БД повернеться (перевірено на стенді).
    /// </summary>
    protected virtual TimeSpan RetryDelay(int attempt) => attempt switch
    {
        1 => TimeSpan.FromSeconds(2),
        2 => TimeSpan.FromSeconds(10),
        _ => TimeSpan.FromSeconds(30)
    };

    /// <summary>Як часто оновлювати gauge глибини DLQ.</summary>
    protected virtual TimeSpan DlqPollInterval => TimeSpan.FromSeconds(15);

    /// <summary>Зареєструвати тип події: за BasicProperties.Type = typeof(TEvent).Name повідомлення десеріалізується в TEvent
    /// і передається IIntegrationEventHandler&lt;TEvent&gt; з DI.</summary>
    protected void Handle<TEvent>() where TEvent : IntegrationEvent =>
        _routes[typeof(TEvent).Name] = new EventRoute(typeof(TEvent),
            (sp, e, ct) => sp.GetRequiredService<IIntegrationEventHandler<TEvent>>().HandleAsync((TEvent)e, ct));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _stopping = stoppingToken;
        var s = Subscription;

        // Канал, топологія, BasicQos, споживач з autoAck: false. Брокер міг ще не піднятися — пробуємо з паузою.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                _channel = await _connection.CreateChannelAsync(cancellationToken: stoppingToken);
                _retryChannel = await _connection.CreateChannelAsync(ConfirmedChannel, stoppingToken);
                await EventBusTopology.DeclareConsumerAsync(_channel, s, stoppingToken);
                await _channel.BasicQosAsync(prefetchSize: 0, prefetchCount: PrefetchCount, global: false, stoppingToken);

                var consumer = new AsyncEventingBasicConsumer(_channel);
                consumer.ReceivedAsync += OnMessageAsync;
                _consumerTag = await _channel.BasicConsumeAsync(s.Queue, autoAck: false, consumer, stoppingToken);
                break;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Consumer {Queue}: не вдалося підключитися до брокера (спроба {Attempt}), повтор через 5 с", s.Queue, attempt);
                await DisposeChannelsAsync();
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        _logger.LogInformation("Consumer {Queue} started: exchange {Exchange}, bindings {RoutingKeys}, prefetch {Prefetch}, events {EventTypes}",
            s.Queue, s.Exchange, s.RoutingKeys, PrefetchCount, _routes.Keys);

        // Далі споживання йде в колбеках; цей цикл лише моніторить глибину DLQ до зупинки застосунку
        await using var monitor = await _connection.CreateChannelAsync(cancellationToken: stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var depth = await monitor.MessageCountAsync(s.DeadLetterQueue, stoppingToken);
                _metrics.SetDlqDepth(s.DeadLetterQueue, depth);
                if (depth > 0)
                    _logger.LogWarning("DLQ {DeadLetterQueue} contains {DlqDepth} messages — потрібне ручне розслідування", s.DeadLetterQueue, depth);
                await Task.Delay(DlqPollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "DLQ {DeadLetterQueue}: не вдалося прочитати глибину", s.DeadLetterQueue);
                await Task.Delay(DlqPollInterval, stoppingToken).ContinueWith(_ => { }, TaskScheduler.Default);
            }
        }
    }

    /// <summary>
    /// Коректна зупинка: скасувати підписку (нові повідомлення не надходять), дочекатися повідомлень в обробці,
    /// закрити канали. Непідтверджені повідомлення брокер поверне в чергу — їх обробить наступний запуск.
    /// </summary>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true } && _consumerTag is not null)
        {
            try { await _channel.BasicCancelAsync(_consumerTag, cancellationToken: cancellationToken); }
            catch (Exception ex) { _logger.LogDebug(ex, "BasicCancel {ConsumerTag}", _consumerTag); }
        }

        while (Volatile.Read(ref _inFlight) > 0 && !cancellationToken.IsCancellationRequested)
            await Task.Delay(50, CancellationToken.None);

        await base.StopAsync(cancellationToken);
        await DisposeChannelsAsync();
        _logger.LogInformation("Consumer {Queue} stopped", Subscription.Queue);
    }

    private async Task OnMessageAsync(object sender, BasicDeliverEventArgs ea)
    {
        Interlocked.Increment(ref _inFlight);
        var sw = Stopwatch.StartNew();
        var queue = Subscription.Queue;
        var props = ea.BasicProperties;
        var eventType = props.Type ?? "unknown";
        var correlationId = TracePropagation.HeaderValue(props.Headers is { } h && h.TryGetValue(MessageHeaders.CorrelationId, out var cid) ? cid : null) ?? props.CorrelationId;
        var retryCount = ReadRetryCount(props.Headers);

        using var activity = TracePropagation.StartConsumerActivity(ea, queue);
        activity?.SetTag("correlation.id", correlationId);
        activity?.SetTag("messaging.retry_count", retryCount);
        // Структурований контекст для ВСІХ логів обробки (у т. ч. логів обробника, SQL / Mongo): фільтрація в Dashboard
        using var logScope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["EventId"] = props.MessageId,
            ["EventType"] = eventType,
            ["CorrelationId"] = correlationId,
            ["MessagingQueue"] = queue,
            ["RoutingKey"] = ea.RoutingKey,
            ["RetryCount"] = retryCount
        });

        try
        {
            if (!_routes.TryGetValue(eventType, out var route))
                throw new NonRetryableEventException($"Невідомий тип події «{eventType}» для черги {queue}");

            IntegrationEvent @event;
            try
            {
                @event = (IntegrationEvent)EventJson.Deserialize(ea.Body.Span, route.Type);
            }
            catch (JsonException ex)
            {
                throw new NonRetryableEventException($"Некоректний JSON {eventType}: {ex.Message}", ex);
            }
            if (@event.EventId == Guid.Empty)
                throw new NonRetryableEventException("Подія без EventId — ідемпотентність неможлива");

            _logger.LogInformation("Received {EventType} {EventId} from {MessagingQueue} (routing key {RoutingKey}, retry {RetryCount}, redelivered {Redelivered})",
                eventType, @event.EventId, queue, ea.RoutingKey, retryCount, ea.Redelivered);

            await using var scope = _scopes.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<MessageContext>();
            context.EventId = @event.EventId;
            context.EventType = eventType;
            context.Queue = queue;
            context.CorrelationId = correlationId ?? @event.CorrelationId;
            context.RetryCount = retryCount;
            context.Redelivered = ea.Redelivered;

            // Обробка не скасовується токеном зупинки: розпочата транзакція має завершитися (StopAsync чекає in-flight)
            await route.Invoke(scope.ServiceProvider, @event, CancellationToken.None);

            await _channel!.BasicAckAsync(ea.DeliveryTag, multiple: false);
            var result = context.IsDuplicate ? "duplicate" : "ack";
            _metrics.Consumed(queue, eventType, result, sw.Elapsed.TotalMilliseconds);
            _logger.LogInformation("Acked {EventType} {EventId} ({Result}) in {ElapsedMs} ms", eventType, @event.EventId, result, sw.ElapsedMilliseconds);
        }
        catch (NonRetryableEventException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            await DeadLetterAsync(ea, eventType, retryCount, ex, "permanent failure", sw);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            await RetryOrDeadLetterAsync(ea, eventType, retryCount, ex, sw);
        }
        finally
        {
            Interlocked.Decrement(ref _inFlight);
        }
    }

    private async Task RetryOrDeadLetterAsync(BasicDeliverEventArgs ea, string eventType, int retryCount, Exception ex, Stopwatch sw)
    {
        if (retryCount >= MaxRetries)
        {
            await DeadLetterAsync(ea, eventType, retryCount, ex, $"retries exhausted ({retryCount}/{MaxRetries})", sw);
            return;
        }

        var attempt = retryCount + 1;
        var delay = RetryDelay(attempt);
        _logger.LogWarning(ex, "Message {EventId} ({EventType}) retry {Attempt}/{MaxRetries} in {DelayMs} ms: {Reason}",
            ea.BasicProperties.MessageId, eventType, attempt, MaxRetries, delay.TotalMilliseconds, ex.Message);
        try
        {
            await Task.Delay(delay, _stopping);

            // Копія з тим самим тілом, MessageId і traceparent, але зі збільшеним лічильником. Публікуємо через default exchange
            // прямо в СВОЮ чергу: повтор у вихідний exchange продублював би подію в черги інших сервісів.
            var props = new BasicProperties(ea.BasicProperties)
            {
                Headers = new Dictionary<string, object?>(ea.BasicProperties.Headers ?? new Dictionary<string, object?>())
                {
                    [MessageHeaders.RetryCount] = attempt,
                    [MessageHeaders.LastError] = Truncate($"{ex.GetType().Name}: {ex.Message}", 500)
                }
            };
            await _retryChannel!.BasicPublishAsync(exchange: "", routingKey: Subscription.Queue, mandatory: false, props, ea.Body);
            await _channel!.BasicAckAsync(ea.DeliveryTag, multiple: false);   // оригінал — лише ПІСЛЯ підтвердженої копії
            _metrics.Consumed(Subscription.Queue, eventType, "retry", sw.Elapsed.TotalMilliseconds);
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            // Зупинка під час очікування: повертаємо оригінал у чергу — його обробить наступний запуск
            await _channel!.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true);
        }
        catch (Exception publishEx)
        {
            // Не вдалося перепублікувати — повертаємо оригінал у чергу (лічильник не зросте, але повідомлення не загубиться)
            _logger.LogError(publishEx, "Retry publish failed for {EventId}, requeue original", ea.BasicProperties.MessageId);
            await _channel!.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true);
        }
    }

    private async Task DeadLetterAsync(BasicDeliverEventArgs ea, string eventType, int retryCount, Exception ex, string reason, Stopwatch sw)
    {
        _logger.LogError(ex, "Message {EventId} ({EventType}) sent to DLQ {DeadLetterQueue}: {DeadLetterReason}, retries {RetryCount}, error {ErrorMessage}",
            ea.BasicProperties.MessageId, eventType, Subscription.DeadLetterQueue, reason, retryCount, ex.Message);
        await _channel!.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false);   // → x-dead-letter-exchange → DLQ
        _metrics.Consumed(Subscription.Queue, eventType, "dead_letter", sw.Elapsed.TotalMilliseconds);
    }

    /// <summary>x-retry-count: int, якщо його записав .NET-клієнт; long / byte[] — якщо інший клієнт або Management UI.</summary>
    public static int ReadRetryCount(IDictionary<string, object?>? headers)
    {
        if (headers is null || !headers.TryGetValue(MessageHeaders.RetryCount, out var value)) return 0;
        return value switch
        {
            int i => i,
            long l => (int)l,
            byte b => b,
            short sh => sh,
            byte[] bytes when int.TryParse(System.Text.Encoding.UTF8.GetString(bytes), out var parsed) => parsed,
            string str when int.TryParse(str, out var parsed) => parsed,
            _ => 0
        };
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    private async Task DisposeChannelsAsync()
    {
        foreach (var channel in new[] { _channel, _retryChannel })
        {
            if (channel is null) continue;
            try
            {
                if (channel.IsOpen) await channel.CloseAsync();
                await channel.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Channel close");
            }
        }
        _channel = _retryChannel = null;
    }

    private sealed record EventRoute(Type Type, Func<IServiceProvider, object, CancellationToken, Task> Invoke);
}
