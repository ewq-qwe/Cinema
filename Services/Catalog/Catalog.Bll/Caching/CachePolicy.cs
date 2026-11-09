namespace Catalog.Bll.Caching;

/// <summary>
/// Час життя запису на двох рівнях. L1 (пам'ять процесу) завжди коротший за L2 (Redis):
/// інакше застаріле значення в пам'яті пережило б оновлення в Redis.
/// </summary>
public sealed record CachePolicy(TimeSpan? L1Sliding, TimeSpan L1Absolute, TimeSpan L2Absolute)
{
    /// <summary>Картка фільму: часто читається, змінюється рідко — sliding у пам'яті, absolute у Redis.</summary>
    public static readonly CachePolicy MovieCard = new(TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30));

    /// <summary>Довідник жанрів: майже не змінюється — лише absolute expiration.</summary>
    public static readonly CachePolicy Genres = new(null, TimeSpan.FromMinutes(10), TimeSpan.FromHours(6));

    /// <summary>Сторінки списку фільмів: комбінацій фільтрів багато — короткий TTL.</summary>
    public static readonly CachePolicy MovieList = new(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(10));

    /// <summary>Версія списків (масова інвалідація): у пам'яті недовго, щоб репліки швидко побачили нову версію.</summary>
    public static readonly CachePolicy Version = new(null, TimeSpan.FromSeconds(30), TimeSpan.FromDays(1));
}
