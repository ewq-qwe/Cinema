using Catalog.Bll.Dtos;
using Catalog.Bll.Specifications;
using Catalog.Domain.Entities;

namespace Catalog.Tests;

/// <summary>Специфікація не залежить від EF — її можна перевірити на колекції в пам'яті.</summary>
public class SpecificationTests
{
    private static readonly List<Movie> Movies =
    [
        new("Карпатський експрес", 2025, 118, "12+", 1),
        new("Таємниця старого маяка", 2024, 104, "12+", 2),
        new("Мандрівка до зірок", 2025, 92, "0+", 3),
        new("Остання зміна", 2023, 131, "16+", 4),
        new("Місто без світла", 2024, 109, "16+", 5)
    ];

    [Fact]
    public void Filters_by_year_range_and_sorts_by_duration_desc()
    {
        var spec = new MoviesFilterSpec(new MovieQuery { YearFrom = 2024, SortBy = "duration", SortDir = "desc" }, 1, 10);

        var result = spec.Evaluate(Movies).Select(m => m.Title).ToList();

        Assert.Equal(["Карпатський експрес", "Місто без світла", "Таємниця старого маяка", "Мандрівка до зірок"], result);
    }

    [Fact]
    public void Applies_paging_after_filtering_and_sorting()
    {
        var spec = new MoviesFilterSpec(new MovieQuery { SortBy = "title" }, page: 2, pageSize: 2);

        var result = spec.Evaluate(Movies).Select(m => m.Title).ToList();

        // за назвою: Карпатський, Мандрівка, Місто, Остання, Таємниця → 2-га сторінка по 2
        Assert.Equal(["Місто без світла", "Остання зміна"], result);
    }

    [Fact]
    public void Second_specification_uses_its_own_criteria()
    {
        List<Person> people = [new("Олег Кравець", 1988), new("Марта Савчук", 1995), new("Софія Лисенко", 2001)];
        var spec = new PeopleFilterSpec(new PersonQuery { BornFrom = 1990, SortBy = "birthYear", SortDir = "desc" }, 1, 10);

        Assert.Equal(["Софія Лисенко", "Марта Савчук"], spec.Evaluate(people).Select(p => p.FullName));
    }
}
