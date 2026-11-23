namespace Reviews.Application.Common.Interfaces;

/// <summary>
/// Лаб. 08: оновлення денормалізованої назви фільму (movieTitle) подіями з Каталогу + ідемпотентність споживача.
/// Реалізація — Infrastructure (MongoDB): колекції processedEvents і catalogMovies.
/// </summary>
public interface ICatalogSyncRepository
{
    /// <summary>EventId уже є в processedEvents — подію оброблено раніше.</summary>
    Task<bool> IsProcessedAsync(Guid eventId, CancellationToken ct);

    /// <summary>Записати EventId як оброблений (унікальний _id; повторний запис — не помилка).</summary>
    Task MarkProcessedAsync(Guid eventId, string eventType, CancellationToken ct);

    /// <summary>
    /// Зафіксувати нову назву фільму в catalogMovies, якщо подія НЕ застаріла (out-of-order).
    /// false — у сервісі вже застосовано новішу зміну назви, цю подію треба пропустити.
    /// </summary>
    Task<bool> TryAdvanceTitleAsync(int movieId, string title, DateTime occurredAt, CancellationToken ct);

    /// <summary>Id відгуків фільму — для інвалідації їх кешу після перейменування.</summary>
    Task<IReadOnlyList<string>> GetReviewIdsAsync(int movieId, CancellationToken ct);

    /// <summary>Оновити movieTitle в УСІХ відгуках і обговореннях фільму (updateMany).</summary>
    Task<(long Reviews, long Discussions)> RenameMovieAsync(int movieId, string title, CancellationToken ct);
}
