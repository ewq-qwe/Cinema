using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Platform.EventBus.Abstractions;
using Platform.EventBus.Consuming;
using Platform.EventBus.Diagnostics;
using Platform.EventBus.Publishing;
using Platform.ServiceDefaults.Correlation;
using RabbitMQ.Client;

namespace Platform.EventBus;

/// <summary>
/// Реєстрація шини подій у сервісі. Підключення до брокера реєструє сам сервіс у Program.cs:
/// <c>builder.AddRabbitMQClient("messaging", s => s.DisableTracing = true)</c> — IConnection (singleton) з health check;
/// вбудоване трасування клієнта вимкнено, бо контекст передається вручну (<see cref="TracePropagation"/>).
/// </summary>
public static class EventBusExtensions
{
    /// <summary>Ім'я ресурсу RabbitMQ в AppHost = ім'я рядка підключення (ConnectionStrings:messaging).</summary>
    public const string ConnectionName = "messaging";

    /// <param name="services">DI.</param>
    /// <param name="serviceName">Ім'я сервісу-публікатора (AppId і заголовок x-source повідомлень).</param>
    public static IServiceCollection AddEventBus(this IServiceCollection services, string serviceName)
    {
        services.AddHttpContextAccessor();
        services.AddSingleton<MessagingMetrics>();
        services.AddSingleton<ICorrelationIdProvider, CorrelationIdProvider>();
        services.AddSingleton<IEventPublisher>(sp => new RabbitMqEventPublisher(
            sp.GetRequiredService<IConnection>(), sp.GetRequiredService<MessagingMetrics>(),
            sp.GetRequiredService<ILogger<RabbitMqEventPublisher>>(), serviceName));
        services.AddScoped<MessageContext>();
        return services;
    }

    /// <summary>Окремий обробник на тип події; Scoped — свої залежності (UoW, репозиторії) на кожне повідомлення.</summary>
    public static IServiceCollection AddEventHandler<TEvent, THandler>(this IServiceCollection services)
        where TEvent : IntegrationEvent
        where THandler : class, IIntegrationEventHandler<TEvent> =>
        services.AddScoped<IIntegrationEventHandler<TEvent>, THandler>();
}

/// <summary>CorrelationId для нової події: з вхідного HTTP-запиту, з повідомлення, що обробляється, або з тегу поточного span.</summary>
public interface ICorrelationIdProvider
{
    string? Current { get; }
}

internal sealed class CorrelationIdProvider(IHttpContextAccessor http) : ICorrelationIdProvider
{
    public string? Current =>
        http.HttpContext?.Request.Headers[CorrelationIdMiddleware.HeaderName].ToString() is { Length: > 0 } fromRequest
            ? fromRequest
            : Activity.Current?.GetTagItem(CorrelationIdMiddleware.SpanTag) as string
              ?? Activity.Current?.Parent?.GetTagItem(CorrelationIdMiddleware.SpanTag) as string;
}
