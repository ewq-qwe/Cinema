namespace Catalog.Bll.Services;

public static class Paging
{
    public const int DefaultPageSize = 10;
    public const int MaxPageSize = 50;

    /// <summary>page — з 1; pageSize — за замовчуванням 10, не більше 50.</summary>
    public static (int Page, int PageSize) Normalize(int page, int pageSize) =>
        (Math.Max(page, 1), pageSize < 1 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize));
}
