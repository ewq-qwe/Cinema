using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Reviews.Application.Common.Caching;
using Reviews.Application.Common.Interfaces;
using Reviews.Application.Movies.Commands.SyncMovieTitle;

namespace Reviews.Tests;

/// <summary>Лаб. 08: оновлення movieTitle подіями Каталогу — ідемпотентність і захист від out-of-order.</summary>
public class CatalogSyncTests
{
    private readonly ICatalogSyncRepository _sync = Substitute.For<ICatalogSyncRepository>();
    private readonly ITwoLevelCache _cache = Substitute.For<ITwoLevelCache>();
    private readonly SyncMovieTitleCommandHandler _handler;

    public CatalogSyncTests() =>
        _handler = new SyncMovieTitleCommandHandler(_sync, _cache, NullLogger<SyncMovieTitleCommandHandler>.Instance);

    private static SyncMovieTitleCommand Command() => new(Guid.NewGuid(), 3, "Нова назва", DateTime.UtcNow);

    [Fact]
    public async Task New_event_renames_all_reviews_invalidates_cache_and_records_EventId()
    {
        var cmd = Command();
        _sync.TryAdvanceTitleAsync(3, "Нова назва", cmd.OccurredAt, Arg.Any<CancellationToken>()).Returns(true);
        _sync.GetReviewIdsAsync(3, Arg.Any<CancellationToken>()).Returns(["r1", "r2"]);
        _sync.RenameMovieAsync(3, "Нова назва", Arg.Any<CancellationToken>()).Returns((2L, 1L));

        var outcome = await _handler.Handle(cmd, TestContext.Current.CancellationToken);

        Assert.Equal(SyncMovieTitleOutcome.Applied, outcome);
        await _cache.Received(1).RemoveAsync(ReviewsCacheKeys.Review("r1"), "MovieRenamed", Arg.Any<CancellationToken>());
        await _cache.Received(1).RemoveAsync(ReviewsCacheKeys.MovieRating(3), "MovieRenamed", Arg.Any<CancellationToken>());
        await _cache.Received(1).BumpVersionAsync(ReviewsCacheKeys.ListVersion, "MovieRenamed", Arg.Any<CancellationToken>());
        Received.InOrder(() =>
        {
            _sync.RenameMovieAsync(3, "Нова назва", Arg.Any<CancellationToken>());
            _sync.MarkProcessedAsync(cmd.EventId, "MovieRenamedEvent", Arg.Any<CancellationToken>());   // лише після бізнес-змін
        });
    }

    [Fact]
    public async Task Duplicate_event_changes_nothing()
    {
        var cmd = Command();
        _sync.IsProcessedAsync(cmd.EventId, Arg.Any<CancellationToken>()).Returns(true);

        var outcome = await _handler.Handle(cmd, TestContext.Current.CancellationToken);

        Assert.Equal(SyncMovieTitleOutcome.Duplicate, outcome);
        await _sync.DidNotReceiveWithAnyArgs().RenameMovieAsync(default, default!, default);
        await _sync.DidNotReceiveWithAnyArgs().MarkProcessedAsync(default, default!, default);
    }

    [Fact]
    public async Task Stale_out_of_order_event_does_not_overwrite_newer_title()
    {
        var cmd = Command();
        _sync.TryAdvanceTitleAsync(3, "Нова назва", cmd.OccurredAt, Arg.Any<CancellationToken>()).Returns(false);

        var outcome = await _handler.Handle(cmd, TestContext.Current.CancellationToken);

        Assert.Equal(SyncMovieTitleOutcome.Stale, outcome);
        await _sync.DidNotReceiveWithAnyArgs().RenameMovieAsync(default, default!, default);
        await _sync.Received(1).MarkProcessedAsync(cmd.EventId, "MovieRenamedEvent", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Validator_rejects_event_without_title()
    {
        var result = new SyncMovieTitleCommandValidator().Validate(new SyncMovieTitleCommand(Guid.NewGuid(), 3, "", DateTime.UtcNow));
        Assert.False(result.IsValid);
    }
}
