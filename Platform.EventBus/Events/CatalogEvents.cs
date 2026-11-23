using Platform.EventBus.Abstractions;

namespace Platform.EventBus.Events;

// Лаб. 08: контракти подій Каталогу — публічна «мова» між сервісами (публікує catalog-api, споживають booking-api і reviews-api).
// Кожна подія самодостатня: несе ПОВНИЙ знімок полів, які копіюють інші сервіси (політика дублювання з Лаб. 01),
// тож споживач може створити копію навіть тоді, коли пропустив попередні події, і не звертається до Каталогу.
// Назви — у минулому часі (бізнес-факт уже стався). Аналог ProductPriceChangedIntegrationEvent з eShop — MovieScreeningInfoChangedEvent.

/// <summary>У каталог додано фільм — Бронювання створює рядок у локальному довіднику Movies.</summary>
public sealed record MovieCreatedEvent(
    int MovieId,
    string Title,
    int ReleaseYear,
    int DurationMinutes,
    string AgeRating) : IntegrationEvent;

/// <summary>
/// Фільм перейменовано — Бронювання оновлює Movies.Title (але НЕ Bookings.MovieTitle: це знімок на квитку),
/// Відгуки — movieTitle в усіх відгуках і обговореннях фільму.
/// </summary>
public sealed record MovieRenamedEvent(
    int MovieId,
    string OldTitle,
    string NewTitle,
    int DurationMinutes,
    string AgeRating) : IntegrationEvent;

/// <summary>
/// Змінилися дані показу (тривалість і / або віковий рейтинг) — Бронювання оновлює копію в Movies.
/// Ціни вже проданих квитків і знімки в бронюваннях не змінюються (політика з Лаб. 01).
/// </summary>
public sealed record MovieScreeningInfoChangedEvent(
    int MovieId,
    string Title,
    int OldDurationMinutes,
    int NewDurationMinutes,
    string OldAgeRating,
    string NewAgeRating) : IntegrationEvent;

/// <summary>Фільм перенесено в архів (м'яке видалення) — у Бронюванні на нього більше не можна створювати сеанси.</summary>
public sealed record MovieArchivedEvent(
    int MovieId,
    string Title) : IntegrationEvent;

/// <summary>Топологія подій Каталогу: exchange і ключі маршрутизації.</summary>
public static class CatalogEventRoutes
{
    /// <summary>Topic exchange подій Каталогу.</summary>
    public const string Exchange = "catalog-events";

    public const string MovieCreated = "movie.created";
    public const string MovieRenamed = "movie.renamed";
    public const string MovieScreeningInfoChanged = "movie.screening_changed";
    public const string MovieArchived = "movie.archived";

    /// <summary>Шаблон «усі події фільмів» (* — рівно одне слово після крапки).</summary>
    public const string AllMovieEvents = "movie.*";
}
