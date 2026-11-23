using RabbitMQ.Client;

namespace Platform.EventBus.Topology;

/// <summary>Підписка сервісу-споживача: власна черга, exchange-джерело і шаблони ключів маршрутизації.</summary>
/// <param name="Queue">Описова назва черги: &lt;сервіс&gt;.&lt;призначення&gt;, напр. booking.catalog-sync.</param>
/// <param name="Exchange">Topic exchange, з якого черга отримує події.</param>
/// <param name="RoutingKeys">Bindings: * — одне слово, # — будь-яка кількість слів.</param>
public sealed record QueueSubscription(string Queue, string Exchange, IReadOnlyList<string> RoutingKeys)
{
    /// <summary>Черга «мертвих» повідомлень цього споживача.</summary>
    public string DeadLetterQueue => EventBusTopology.DeadLetterQueueName(Queue);

    /// <summary>Dead letter exchange для подій цього exchange.</summary>
    public string DeadLetterExchange => EventBusTopology.DeadLetterExchangeName(Exchange);
}

/// <summary>
/// Декларативна топологія: оголошується в коді під час старту (publisher — exchange, consumer — свою чергу).
/// Повторне оголошення з тими самими параметрами безпечне (ідемпотентне); змінювати параметри вже створеної
/// черги вручну в Management UI не можна — наступне оголошення завершиться PRECONDITION_FAILED.
/// </summary>
/// <remarks>
/// <code>
/// catalog-events (topic, durable) ──movie.*──────► booking.catalog-sync (durable, x-dead-letter-exchange)
///                                 └─movie.renamed─► reviews.catalog-sync (durable, x-dead-letter-exchange)
/// catalog-events.dlx (direct) ──booking.catalog-sync──► booking.catalog-sync.dlq
///                             └─reviews.catalog-sync──► reviews.catalog-sync.dlq
/// </code>
/// DLX типу direct, а ключ dead-lettering = назва черги: кожен споживач отримує в DLQ лише СВОЇ
/// невдалі повідомлення (з fanout-DLX кожна DLQ збирала б відмови всіх черг).
/// </remarks>
public static class EventBusTopology
{
    public static string DeadLetterExchangeName(string exchange) => $"{exchange}.dlx";

    public static string DeadLetterQueueName(string queue) => $"{queue}.dlq";

    /// <summary>Topic exchange подій (durable — переживає перезапуск брокера). Оголошує publisher.</summary>
    public static Task DeclareExchangeAsync(IChannel channel, string exchange, CancellationToken ct) =>
        channel.ExchangeDeclareAsync(exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: ct);

    /// <summary>
    /// Повна топологія споживача: exchange, DLX, основна черга з x-dead-letter-exchange, bindings, DLQ і її binding.
    /// </summary>
    public static async Task DeclareConsumerAsync(IChannel channel, QueueSubscription s, CancellationToken ct)
    {
        await DeclareExchangeAsync(channel, s.Exchange, ct);
        await channel.ExchangeDeclareAsync(s.DeadLetterExchange, ExchangeType.Direct, durable: true, autoDelete: false, cancellationToken: ct);

        // DLQ: сюди RabbitMQ переносить відхилені (BasicNack requeue:false) повідомлення з заголовком x-death
        await channel.QueueDeclareAsync(s.DeadLetterQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
        await channel.QueueBindAsync(s.DeadLetterQueue, s.DeadLetterExchange, routingKey: s.Queue, cancellationToken: ct);

        await channel.QueueDeclareAsync(s.Queue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = s.DeadLetterExchange,
                ["x-dead-letter-routing-key"] = s.Queue
            },
            cancellationToken: ct);
        foreach (var key in s.RoutingKeys)
            await channel.QueueBindAsync(s.Queue, s.Exchange, key, cancellationToken: ct);
    }
}
