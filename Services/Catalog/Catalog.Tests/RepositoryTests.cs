using Catalog.Bll.Dtos;
using Catalog.Bll.Specifications;
using Catalog.Dal;
using Catalog.Domain.Entities;
using Catalog.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Tests;

/// <summary>Репозиторії й Unit of Work на провайдері EF Core InMemory (без SQL Server).</summary>
public class RepositoryTests
{
    private static CatalogDbContext NewContext() =>
        new(new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static async Task<CatalogDbContext> SeededAsync()
    {
        var db = NewContext();
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);   // застосовує HasData жанрів
        var actor = new Person("Олег Кравець", 1988);
        var m1 = new Movie("Карпатський експрес", 2025, 118, "12+", 1);
        var m2 = new Movie("Місто без світла", 2024, 109, "16+", 5);
        var m3 = new Movie("Ще одна пригода", 2023, 95, "6+", 1);
        m1.AddCredit(actor, CreditRole.Actor, "Мирон", 1);
        m2.AddCredit(actor, CreditRole.Actor, "детектив Шевчук", 2);
        db.AddRange(actor, m1, m2, m3);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        db.ChangeTracker.Clear();
        return db;
    }

    [Fact]
    public async Task Count_by_specification_returns_all_matching_rows_not_page_size()
    {
        await using var db = await SeededAsync();
        var uow = new UnitOfWork(db);
        var spec = new MoviesFilterSpec(new MovieQuery(), page: 2, pageSize: 1);

        var total = await uow.Movies.CountAsync(spec, TestContext.Current.CancellationToken);
        var page = await uow.Movies.ListAsync(spec, TestContext.Current.CancellationToken);

        Assert.Equal(3, total);
        Assert.Single(page);
    }

    [Fact]
    public async Task Explicit_loading_loads_genre_movies_on_demand()
    {
        await using var db = await SeededAsync();
        var uow = new UnitOfWork(db);

        var genre = await uow.Genres.GetWithMoviesAsync(1, TestContext.Current.CancellationToken);

        Assert.NotNull(genre);
        Assert.Equal(["Карпатський експрес", "Ще одна пригода"], genre.Movies.Select(m => m.Title));
    }

    [Fact]
    public async Task Eager_loading_returns_movie_with_credits_and_people()
    {
        await using var db = await SeededAsync();
        var uow = new UnitOfWork(db);
        var id = await db.Movies.Where(m => m.Title == "Карпатський експрес").Select(m => m.Id).SingleAsync(TestContext.Current.CancellationToken);

        var movie = await uow.Movies.GetWithDetailsAsync(id, TestContext.Current.CancellationToken);

        Assert.Equal("Пригоди", movie!.Genre.Name);
        Assert.Equal("Олег Кравець", movie.Credits.Single().Person.FullName);
    }

    [Fact]
    public async Task Filmography_goes_through_join_entity()
    {
        await using var db = await SeededAsync();
        var uow = new UnitOfWork(db);
        var personId = await db.People.Select(p => p.Id).SingleAsync(TestContext.Current.CancellationToken);

        var films = await uow.MovieCredits.GetFilmographyAsync(personId, TestContext.Current.CancellationToken);
        var leads = await uow.MovieCredits.GetLeadActorsAsync("Пригоди", 1, TestContext.Current.CancellationToken);

        Assert.Equal(["Карпатський експрес", "Місто без світла"], films.Select(f => f.Title));
        Assert.Equal("Олег Кравець", Assert.Single(leads).FullName);
    }

    [Fact]
    public async Task Archived_movies_are_hidden_by_query_filter()
    {
        await using var db = await SeededAsync();
        var uow = new UnitOfWork(db);
        var movie = await db.Movies.FirstAsync(m => m.Title == "Ще одна пригода", TestContext.Current.CancellationToken);

        movie.Archive();
        await uow.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, await uow.Movies.CountAsync(new MoviesFilterSpec(new MovieQuery(), 1, 10), TestContext.Current.CancellationToken));
        Assert.True(await uow.Movies.ExistsAsync("Ще одна пригода", 2023, ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Entity_rule_violation_surfaces_as_DomainException()
    {
        await using var db = await SeededAsync();
        var movie = await new UnitOfWork(db).Movies.GetWithDetailsAsync(
            await db.Movies.Where(m => m.Title == "Карпатський експрес").Select(m => m.Id).SingleAsync(TestContext.Current.CancellationToken),
            TestContext.Current.CancellationToken);
        var actor = movie!.Credits.Single().Person;

        Assert.Throws<DomainException>(() => movie.AddCredit(actor, CreditRole.Actor, "Двійник", 3));
    }
}
