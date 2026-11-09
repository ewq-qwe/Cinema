using System.Diagnostics;

namespace Reviews.Application.Common.Diagnostics;

/// <summary>
/// Власні spans сценаріїв використання (Лаб. 05). Джерело підключено до OpenTelemetry в ServiceDefaults:
/// span «review.create» обгортає команди MongoDB цієї операції в trace.
/// </summary>
public static class ReviewsTelemetry
{
    public const string SourceName = "Reviews.Application";

    public static readonly ActivitySource Source = new(SourceName);
}
