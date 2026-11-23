namespace Reviews.Application.Common.Caching;

/// <summary>Час життя запису на двох рівнях; L1 (пам'ять) завжди коротший за L2 (Redis).</summary>
public sealed record CachePolicy(TimeSpan? L1Sliding, TimeSpan L1Absolute, TimeSpan L2Absolute)
{
    /// <summary>Рейтинг фільму — агрегація по всіх відгуках: дорогий запит, читається на кожній картці фільму.</summary>
    public static readonly CachePolicy MovieRating = new(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(10));

    /// <summary>Відгук з коментарями.</summary>
    public static readonly CachePolicy Review = new(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(15));

    /// <summary>Сторінки списків відгуків — коротко, бо нові відгуки мають з'являтися швидко.</summary>
    public static readonly CachePolicy ReviewList = new(TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5));

    /// <summary>Версія списків (масова інвалідація).</summary>
    public static readonly CachePolicy Version = new(null, TimeSpan.FromSeconds(30), TimeSpan.FromDays(1));
}

/// <summary>Запит, результат якого можна кешувати: CachingBehavior бере ключ і політику з самого запиту.</summary>
public interface ICacheableQuery
{
    /// <summary>Ключ без версії списків; null — не кешувати цей конкретний запит.</summary>
    string? CacheKey { get; }

    CachePolicy CachePolicy { get; }

    /// <summary>true — ключ належить до групи списків і доповнюється поточною версією (масова інвалідація).</summary>
    bool IsListQuery => false;
}

/// <summary>Схема ключів з префіксом сервісу.</summary>
public static class ReviewsCacheKeys
{
    public const string Prefix = "reviews";

    public static string MovieRating(int movieId) => $"{Prefix}:rating:{movieId}";

    public static string Review(string id) => $"{Prefix}:review:{id}";

    public const string ListVersion = $"{Prefix}:list-version";
}
