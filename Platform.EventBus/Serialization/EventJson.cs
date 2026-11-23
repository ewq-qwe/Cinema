using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using System.Text.Json.Serialization;

namespace Platform.EventBus.Serialization;

/// <summary>
/// Єдиний формат повідомлень для всіх сервісів: System.Text.Json, camelCase, enum — рядком, дати — ISO 8601 (UTC).
/// Однакові правила в publisher і consumer; JSON дає змогу споживати події не лише .NET-сервісам.
/// </summary>
public static class EventJson
{
    public const string ContentType = "application/json";

    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)   // camelCase + регістронезалежне читання
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        // Кирилиця без \uXXXX-екранування: тіло повідомлення читабельне в Management UI (HTML-символи все одно екрануються)
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        // Нові поля, яких споживач ще не знає, ігноруються (зворотна сумісність при додаванні полів)
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip
    };

    public static byte[] Serialize(object @event) => JsonSerializer.SerializeToUtf8Bytes(@event, @event.GetType(), Options);

    /// <summary>Десеріалізація в конкретний тип; null або пошкоджений JSON → <see cref="JsonException"/> (постійний збій → DLQ).</summary>
    public static object Deserialize(ReadOnlySpan<byte> body, Type type) =>
        JsonSerializer.Deserialize(body, type, Options) ?? throw new JsonException($"Порожнє повідомлення для {type.Name}");
}
