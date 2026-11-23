using Booking.Api.IntegrationEvents;
using Booking.Dal.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Platform.EventBus.Abstractions;
using Platform.EventBus.Consuming;
using Platform.EventBus.Events;

namespace Booking.Tests;

/// <summary>Лаб. 08: обробники подій Каталогу — ідемпотентність, транзакція, класифікація збоїв.</summary>
public class IntegrationEventHandlerTests
{
    private readonly IUnitOfWork _uow = TestHelpers.CreateUow();
    private readonly IMovieCopyRepository _movies = Substitute.For<IMovieCopyRepository>();
    private readonly IProcessedEventRepository _processed = Substitute.For<IProcessedEventRepository>();
    private readonly MessageContext _message = new();

    public IntegrationEventHandlerTests()
    {
        _uow.Movies.Returns(_movies);
        _uow.ProcessedEvents.Returns(_processed);
    }

    private MovieRenamedHandler Handler() => new(_uow, _message, NullLogger<MovieRenamedHandler>.Instance);

    private static MovieRenamedEvent Renamed(string rating = "12+") => new(3, "Стара назва", "Нова назва", 118, rating);

    [Fact]
    public async Task New_event_updates_movie_copy_and_records_EventId_in_one_transaction()
    {
        var e = Renamed();
        _uow.ProcessedEvents.TryAddAsync(e.EventId, nameof(MovieRenamedEvent), Arg.Any<CancellationToken>()).Returns(true);
        _uow.Movies.UpsertAsync(Arg.Any<MovieSnapshot>(), e.OccurredAt, Arg.Any<CancellationToken>()).Returns(true);

        await Handler().HandleAsync(e, TestContext.Current.CancellationToken);

        Received.InOrder(() =>
        {
            _uow.BeginTransactionAsync(Arg.Any<System.Data.IsolationLevel>(), Arg.Any<CancellationToken>());
            _processed.TryAddAsync(e.EventId, nameof(MovieRenamedEvent), Arg.Any<CancellationToken>());
            _movies.UpsertAsync(new MovieSnapshot(3, "Нова назва", 118, "12+"), e.OccurredAt, Arg.Any<CancellationToken>());
            _uow.CommitAsync(Arg.Any<CancellationToken>());
        });
        Assert.False(_message.IsDuplicate);
    }

    [Fact]
    public async Task Duplicate_event_is_skipped_without_changes()
    {
        var e = Renamed();
        _uow.ProcessedEvents.TryAddAsync(e.EventId, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);

        await Handler().HandleAsync(e, TestContext.Current.CancellationToken);

        await _uow.Movies.DidNotReceiveWithAnyArgs().UpsertAsync(default!, default, default);
        await _uow.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
        await _uow.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
        Assert.True(_message.IsDuplicate);
    }

    [Fact]
    public async Task Invalid_event_data_is_a_permanent_failure_and_touches_no_data()
    {
        await Assert.ThrowsAsync<NonRetryableEventException>(() =>
            Handler().HandleAsync(Renamed(rating: "21+"), TestContext.Current.CancellationToken));

        await _uow.DidNotReceiveWithAnyArgs().BeginTransactionAsync(default, default);
    }

    [Fact]
    public async Task Transient_database_failure_rolls_back_and_is_rethrown_for_retry()
    {
        var e = Renamed();
        _uow.ProcessedEvents.TryAddAsync(e.EventId, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
        _uow.Movies.UpsertAsync(Arg.Any<MovieSnapshot>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new TimeoutException("SQL Server is not responding"));

        var ex = await Assert.ThrowsAsync<TimeoutException>(() => Handler().HandleAsync(e, TestContext.Current.CancellationToken));

        Assert.IsNotType<NonRetryableEventException>(ex);   // тимчасовий збій → consumer повторить
        await _uow.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Archived_event_marks_copy_archived()
    {
        var e = new MovieArchivedEvent(5, "Фільм");
        _uow.ProcessedEvents.TryAddAsync(e.EventId, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        await new MovieArchivedHandler(_uow, _message, NullLogger<MovieArchivedHandler>.Instance).HandleAsync(e, TestContext.Current.CancellationToken);

        await _uow.Movies.Received(1).ArchiveAsync(5, e.OccurredAt, Arg.Any<CancellationToken>());
        await _uow.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }
}
