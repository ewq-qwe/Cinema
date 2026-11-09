using Catalog.Bll.Dtos;

namespace Catalog.Bll.Caching;

/// <summary>
/// Передбачувана схема ключів з префіксом сервісу (catalog:…) — ізоляція від ключів інших сервісів у спільному Redis
/// і проста інвалідація: картка — catalog:movie:{id}; списки — через ключ-версію (усі сторінки одразу).
/// </summary>
public static class CatalogCacheKeys
{
    public const string Prefix = "catalog";

    public static string Movie(int id) => $"{Prefix}:movie:{id}";

    public const string Genres = $"{Prefix}:genres:all";

    /// <summary>Ключ-версія: зміна будь-якого фільму дає нову версію, і всі закешовані сторінки списку стають недосяжними.</summary>
    public const string MovieListVersion = $"{Prefix}:movies:list-version";

    public static string MovieList(string version, MovieQuery q, int page, int pageSize) =>
        string.Join(':', Prefix, "movies:list", version, page, pageSize,
            q.Search?.Trim().ToLowerInvariant() ?? "", q.GenreId, q.YearFrom, q.YearTo, q.AgeRating ?? "",
            q.SortBy.ToLowerInvariant(), q.SortDir.ToLowerInvariant());
}
