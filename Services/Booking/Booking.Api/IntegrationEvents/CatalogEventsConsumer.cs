using Platform.EventBus.Consuming;
using Platform.EventBus.Diagnostics;
using Platform.EventBus.Events;
using Platform.EventBus.Topology;
using RabbitMQ.Client;

namespace Booking.Api.IntegrationEvents;

/// <summary>
/// Лаб. 08: споживач подій Каталогу в Бронюванні (BackgroundService, ручний ack — див. базовий <see cref="RabbitMqConsumer"/>).
/// Власна черга booking.catalog-sync отримує ВСІ події фільмів (binding movie.*) і оновлює локальну копію dbo.Movies
/// (політика дублювання з Лаб. 01: Movies.Title / DurationMinutes / AgeRating — «копія, оновлюється подіями з Каталогу»).
/// Кілька екземплярів booking-api читали б одну чергу як competing consumers — кожну подію обробляє лише один.
/// </summary>
public sealed class CatalogEventsConsumer : RabbitMqConsumer
{
    public const string Queue = "booking.catalog-sync";

    public CatalogEventsConsumer(IConnection connection, IServiceScopeFactory scopes, MessagingMetrics metrics, ILogger<CatalogEventsConsumer> logger)
        : base(connection, scopes, metrics, logger)
    {
        Handle<MovieCreatedEvent>();
        Handle<MovieRenamedEvent>();
        Handle<MovieScreeningInfoChangedEvent>();
        Handle<MovieArchivedEvent>();
    }

    protected override QueueSubscription Subscription { get; } =
        new(Queue, CatalogEventRoutes.Exchange, [CatalogEventRoutes.AllMovieEvents]);
}
