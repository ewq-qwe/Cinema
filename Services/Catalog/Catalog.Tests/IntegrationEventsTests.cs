using AutoMapper;
using Catalog.Bll.Dtos;
using Catalog.Bll.IntegrationEvents;
using Catalog.Bll.Mapping;
using Catalog.Bll.Services;
using Catalog.Dal.Abstractions;
using Catalog.Domain.Entities;
using Catalog.Domain.Exceptions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Platform.EventBus.Abstractions;
using Platform.EventBus.Events;

namespace Catalog.Tests;

/// <summary>Лаб. 08: які integration events породжує MovieService і коли вони публікуються.</summary>
public class IntegrationEventsTests
{
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly ICatalogIntegrationEventService _events = Substitute.For<ICatalogIntegrationEventService>();
    private readonly Movie _movie = new("Карпатський експрес", 2025, 118, "12+", 1);
    private readonly MovieService _service;

    public IntegrationEventsTests()
    {
        _uow.Movies.Returns(Substitute.For<IMovieRepository>());
        _uow.Genres.Returns(Substitute.For<IGenreRepository>());
        _uow.Movies.GetWithDetailsAsync(7, Arg.Any<CancellationToken>()).Returns(_movie);
        var mapper = new MapperConfiguration(cfg => cfg.AddMaps(typeof(MovieProfile).Assembly), NullLoggerFactory.Instance).CreateMapper();
        _service = new MovieService(_uow, mapper, TestCache.Create(), _events, NullLogger<MovieService>.Instance);
    }

    private static UpdateMovieDto Update(string title = "Карпатський експрес", int duration = 118, string rating = "12+") => new()
    {
        Title = title, ReleaseYear = 2025, DurationMinutes = duration, AgeRating = rating, GenreId = 1,
        RowVersion = Convert.ToBase64String([1, 2, 3])
    };

    [Fact]
    public async Task Rename_adds_MovieRenamedEvent_with_full_snapshot_and_publishes_only_after_save()
    {
        await _service.UpdateAsync(7, Update(title: "Карпатський експрес 2"), TestContext.Current.CancellationToken);

        _events.Received(1).AddEvent(
            Arg.Is<MovieRenamedEvent>(e => e.OldTitle == "Карпатський експрес" && e.NewTitle == "Карпатський експрес 2"
                                           && e.DurationMinutes == 118 && e.AgeRating == "12+"),
            CatalogEventRoutes.MovieRenamed);
        _events.DidNotReceive().AddEvent(Arg.Any<MovieScreeningInfoChangedEvent>(), Arg.Any<string>());
        Received.InOrder(() =>
        {
            _uow.SaveChangesAsync(Arg.Any<CancellationToken>());          // 1) фільм + outbox
            _events.PublishPendingAsync(Arg.Any<CancellationToken>());    // 2) лише потім — брокер
        });
    }

    [Fact]
    public async Task Changing_duration_and_rating_adds_only_screening_event()
    {
        await _service.UpdateAsync(7, Update(duration: 125, rating: "16+"), TestContext.Current.CancellationToken);

        _events.Received(1).AddEvent(
            Arg.Is<MovieScreeningInfoChangedEvent>(e => e.OldDurationMinutes == 118 && e.NewDurationMinutes == 125
                                                        && e.OldAgeRating == "12+" && e.NewAgeRating == "16+"),
            CatalogEventRoutes.MovieScreeningInfoChanged);
        _events.DidNotReceive().AddEvent(Arg.Any<MovieRenamedEvent>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Changing_only_release_year_raises_no_events_because_nothing_is_replicated()
    {
        var dto = Update();
        dto.ReleaseYear = 2024;
        await _service.UpdateAsync(7, dto, TestContext.Current.CancellationToken);

        _events.DidNotReceive().AddEvent(Arg.Any<IntegrationEvent>(), Arg.Any<string>());
    }

    [Fact]
    public async Task Failed_save_does_not_publish_the_event()
    {
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new ConflictException("RowVersion changed"));

        await Assert.ThrowsAsync<ConflictException>(() =>
            _service.UpdateAsync(7, Update(title: "Нова назва"), TestContext.Current.CancellationToken));

        await _events.DidNotReceive().PublishPendingAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Archive_adds_MovieArchivedEvent()
    {
        _uow.Movies.GetByIdAsync(7, Arg.Any<CancellationToken>()).Returns(_movie);

        await _service.ArchiveAsync(7, TestContext.Current.CancellationToken);

        _events.Received(1).AddEvent(Arg.Is<MovieArchivedEvent>(e => e.MovieId == 7), CatalogEventRoutes.MovieArchived);
        await _events.Received(1).PublishPendingAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Integration_events_are_immutable_records_with_identity_and_time()
    {
        var e = new MovieRenamedEvent(1, "A", "B", 90, "12+");
        var copy = e with { CorrelationId = "c-1" };

        Assert.NotEqual(Guid.Empty, e.EventId);
        Assert.Equal(e.EventId, copy.EventId);              // with зберігає EventId і час
        Assert.Equal(e.OccurredAt, copy.OccurredAt);
        Assert.Null(e.CorrelationId);                       // оригінал не змінився
        // Усі властивості подій — лише init (незмінність після створення)
        var properties = typeof(MovieRenamedEvent).GetProperties().Where(p => p.Name != "EqualityContract");
        Assert.All(properties, p => Assert.Contains(typeof(System.Runtime.CompilerServices.IsExternalInit),
            p.SetMethod!.ReturnParameter.GetRequiredCustomModifiers()));
    }
}
