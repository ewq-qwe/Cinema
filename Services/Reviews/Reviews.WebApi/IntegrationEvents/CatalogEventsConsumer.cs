using Platform.EventBus.Consuming;
using Platform.EventBus.Diagnostics;
using Platform.EventBus.Events;
using Platform.EventBus.Topology;
using RabbitMQ.Client;

namespace Reviews.WebApi.IntegrationEvents;

/// <summary>
/// Лаб. 08: споживач подій Каталогу у Відгуках (BackgroundService, ручний ack — базовий <see cref="RabbitMqConsumer"/>).
/// Відгукам потрібна лише назва фільму, тож черга reviews.catalog-sync прив'язана тільки до movie.renamed:
/// topic exchange сам відсіює решту подій (created / screening_changed / archived сюди не потрапляють).
/// </summary>
public sealed class CatalogEventsConsumer : RabbitMqConsumer
{
    public const string Queue = "reviews.catalog-sync";

    public CatalogEventsConsumer(IConnection connection, IServiceScopeFactory scopes, MessagingMetrics metrics, ILogger<CatalogEventsConsumer> logger)
        : base(connection, scopes, metrics, logger)
    {
        Handle<MovieRenamedEvent>();
    }

    protected override QueueSubscription Subscription { get; } =
        new(Queue, CatalogEventRoutes.Exchange, [CatalogEventRoutes.MovieRenamed]);
}
