using Ardalis.Specification;
using Catalog.Bll.Dtos;
using Catalog.Domain.Entities;

namespace Catalog.Bll.Specifications;

/// <summary>
/// Друга специфікація з іншими критеріями (люди за іменем і роком народження).
/// Працює через той самий GenericRepository.ListAsync / CountAsync (WithSpecification) — без змін у репозиторії.
/// </summary>
public sealed class PeopleFilterSpec : Specification<Person>
{
    public PeopleFilterSpec(PersonQuery q, int page, int pageSize)
    {
        Query.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(q.Search))
            Query.Where(p => p.FullName.Contains(q.Search));
        if (q.BornFrom is not null)
            Query.Where(p => p.BirthYear >= q.BornFrom);
        if (q.BornTo is not null)
            Query.Where(p => p.BirthYear <= q.BornTo);

        var desc = string.Equals(q.SortDir, "desc", StringComparison.OrdinalIgnoreCase);
        if (string.Equals(q.SortBy, "birthYear", StringComparison.OrdinalIgnoreCase))
            (desc ? Query.OrderByDescending(p => p.BirthYear) : Query.OrderBy(p => p.BirthYear)).ThenBy(p => p.Id);
        else
            (desc ? Query.OrderByDescending(p => p.FullName) : Query.OrderBy(p => p.FullName)).ThenBy(p => p.Id);

        Query.Skip((page - 1) * pageSize).Take(pageSize);
    }
}
