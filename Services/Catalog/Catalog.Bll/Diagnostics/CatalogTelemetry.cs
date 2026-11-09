using System.Diagnostics;

namespace Catalog.Bll.Diagnostics;

/// <summary>
/// Власні spans бізнес-операцій каталогу (Лаб. 05). Джерело підключено до OpenTelemetry в ServiceDefaults:
/// span «movie.update» обгортає SQL-команди EF Core цієї операції в trace.
/// </summary>
public static class CatalogTelemetry
{
    public const string SourceName = "Catalog.Bll";

    public static readonly ActivitySource Source = new(SourceName);
}
