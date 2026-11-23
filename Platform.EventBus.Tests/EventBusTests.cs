using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Platform.EventBus.Abstractions;
using Platform.EventBus.Consuming;
using Platform.EventBus.Diagnostics;
using Platform.EventBus.Events;
using Platform.EventBus.Serialization;
using Platform.EventBus.Topology;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Platform.EventBus.Tests;

/// <summary>Лаб. 08: формат повідомлень, передача контексту трасування, лічильник повторів, імена топології.</summary>
public class EventBusTests
{
    [Fact]
    public void Events_are_serialized_as_camelCase_json_and_round_trip()
    {
        var e = new MovieRenamedEvent(3, "Стара", "Нова", 118, "12+") { CorrelationId = "corr-1" };

        var json = Encoding.UTF8.GetString(EventJson.Serialize(e));
        var back = (MovieRenamedEvent)EventJson.Deserialize(Encoding.UTF8.GetBytes(json), typeof(MovieRenamedEvent));

        Assert.Contains("\"movieId\":3", json);
        Assert.Contains("\"newTitle\":\"Нова\"", json);
        Assert.Contains("\"eventId\":", json);
        Assert.Contains("\"schemaVersion\":1", json);
        Assert.Equal(e, back);   // record — порівняння за значенням
    }

    [Fact]
    public void Unknown_new_fields_are_ignored_for_backward_compatibility()
    {
        const string json = """{"eventId":"0b5e8f4e-2c7d-4c55-9a3c-1d2e3f4a5b6c","occurredAt":"2026-11-23T10:00:00Z","movieId":3,"title":"Фільм","newField":"from v2"}""";

        var e = (MovieArchivedEvent)EventJson.Deserialize(Encoding.UTF8.GetBytes(json), typeof(MovieArchivedEvent));

        Assert.Equal(3, e.MovieId);
    }

    [Fact]
    public void Malformed_json_throws_JsonException_which_consumer_treats_as_permanent_failure()
    {
        Assert.ThrowsAny<JsonException>(() => EventJson.Deserialize("{not json"u8, typeof(MovieRenamedEvent)));
        Assert.ThrowsAny<JsonException>(() => EventJson.Deserialize("null"u8, typeof(MovieRenamedEvent)));
    }

    [Fact]
    public void Trace_context_crosses_the_broker_in_traceparent_header()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name is TracePropagation.SourceName or "test",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);
        using var source = new ActivitySource("test");

        // Publisher: HTTP-запит → span публікації → заголовки
        var props = new BasicProperties { MessageId = Guid.NewGuid().ToString(), Type = nameof(MovieRenamedEvent) };
        ActivityTraceId traceId;
        ActivitySpanId publishSpan;
        using (var request = source.StartActivity("PUT /api/movies/3"))
        using (var publish = TracePropagation.StartPublishActivity("catalog-events", "movie.renamed", nameof(MovieRenamedEvent), Guid.NewGuid()))
        {
            traceId = request!.TraceId;
            publishSpan = publish!.SpanId;
            TracePropagation.Inject(props);
        }
        Assert.True(props.Headers!.ContainsKey("traceparent"));

        // Брокер повертає рядкові заголовки як byte[]
        var delivered = new BasicProperties(props)
        {
            Headers = props.Headers.ToDictionary(kv => kv.Key, kv => (object?)Encoding.UTF8.GetBytes((string)kv.Value!))
        };
        var ea = new BasicDeliverEventArgs("ctag", 1, false, "catalog-events", "movie.renamed", delivered, ReadOnlyMemory<byte>.Empty);

        using var consume = TracePropagation.StartConsumerActivity(ea, "booking.catalog-sync");

        Assert.NotNull(consume);
        Assert.Equal(ActivityKind.Consumer, consume.Kind);
        Assert.Equal(traceId, consume.TraceId);              // той самий trace, що й HTTP-запит
        Assert.Equal(publishSpan, consume.ParentSpanId);     // нащадок span публікації
        Assert.Equal("booking.catalog-sync", consume.GetTagItem("messaging.destination.name"));
        Assert.Equal("rabbitmq", consume.GetTagItem("messaging.system"));
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData(2, 2)]
    [InlineData(3L, 3)]
    public void Retry_count_header_is_read_from_any_numeric_representation(object? value, int expected)
    {
        var headers = value is null ? null : new Dictionary<string, object?> { [MessageHeaders.RetryCount] = value };
        Assert.Equal(expected, RabbitMqConsumer.ReadRetryCount(headers));
    }

    [Fact]
    public void Retry_count_header_as_bytes_is_parsed()
    {
        var headers = new Dictionary<string, object?> { [MessageHeaders.RetryCount] = "1"u8.ToArray() };
        Assert.Equal(1, RabbitMqConsumer.ReadRetryCount(headers));
    }

    [Fact]
    public void Each_consumer_queue_has_its_own_dead_letter_queue()
    {
        var s = new QueueSubscription("booking.catalog-sync", CatalogEventRoutes.Exchange, [CatalogEventRoutes.AllMovieEvents]);

        Assert.Equal("booking.catalog-sync.dlq", s.DeadLetterQueue);
        Assert.Equal("catalog-events.dlx", s.DeadLetterExchange);
    }
}
