using AutoMapper;
using Catalog.Bll.Dtos;
using Catalog.Bll.Mapping;
using Catalog.Bll.Services;
using Catalog.Bll.Validators;
using Catalog.Dal.Abstractions;
using Catalog.Domain.Entities;
using Catalog.Domain.Exceptions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Catalog.Tests;

public class ServiceAndMappingTests
{
    private static IMapper Mapper() =>
        new MapperConfiguration(cfg => cfg.AddMaps(typeof(MovieProfile).Assembly), NullLoggerFactory.Instance).CreateMapper();

    private static (IUnitOfWork Uow, MovieService Service) Create()
    {
        var uow = Substitute.For<IUnitOfWork>();
        uow.Movies.Returns(Substitute.For<IMovieRepository>());
        uow.Genres.Returns(Substitute.For<IGenreRepository>());
        uow.People.Returns(Substitute.For<IGenericRepository<Person>>());
        return (uow, new MovieService(uow, Mapper(), NullLogger<MovieService>.Instance));
    }

    [Fact]
    public void AutoMapper_configuration_is_valid() =>
        new MapperConfiguration(cfg => cfg.AddMaps(typeof(MovieProfile).Assembly), NullLoggerFactory.Instance)
            .AssertConfigurationIsValid();

    [Fact]
    public async Task Create_with_existing_title_and_year_throws_Conflict_and_saves_nothing()
    {
        var (uow, service) = Create();
        uow.Genres.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(new Genre("Пригоди"));
        uow.Movies.ExistsAsync("Карпатський експрес", 2025, null, Arg.Any<CancellationToken>()).Returns(true);

        await Assert.ThrowsAsync<ConflictException>(() => service.CreateAsync(
            new CreateMovieDto { Title = "Карпатський експрес", ReleaseYear = 2025, DurationMinutes = 118, AgeRating = "12+", GenreId = 1 },
            TestContext.Current.CancellationToken));
        await uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_with_unknown_genre_throws_Validation()
    {
        var (uow, service) = Create();
        uow.Genres.GetByIdAsync(99, Arg.Any<CancellationToken>()).Returns((Genre?)null);

        await Assert.ThrowsAsync<ValidationException>(() => service.CreateAsync(
            new CreateMovieDto { Title = "Новий", ReleaseYear = 2025, DurationMinutes = 90, AgeRating = "0+", GenreId = 99 },
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Get_nonexistent_movie_throws_NotFound()
    {
        var (uow, service) = Create();
        uow.Movies.GetWithDetailsAsync(404, Arg.Any<CancellationToken>()).Returns((Movie?)null);
        await Assert.ThrowsAsync<EntityNotFoundException>(() => service.GetByIdAsync(404, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Validators_reject_unknown_sort_field_and_empty_character_for_actor()
    {
        Assert.False(new MovieQueryValidator().Validate(new MovieQuery { SortBy = "budget" }).IsValid);
        Assert.False(new MovieQueryValidator().Validate(new MovieQuery { YearFrom = 2025, YearTo = 2020 }).IsValid);
        Assert.True(new MovieQueryValidator().Validate(new MovieQuery { SortBy = "year", SortDir = "desc" }).IsValid);
        Assert.False(new AddCreditDtoValidator().Validate(new AddCreditDto { PersonId = 1, Role = "Actor", BillingOrder = 1 }).IsValid);
        Assert.False(new CreateMovieDtoValidator().Validate(new CreateMovieDto { Title = "X", ReleaseYear = 1700, DurationMinutes = 90, AgeRating = "0+", GenreId = 1 }).IsValid);
    }
}
