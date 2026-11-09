using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Reviews.Application;
using Reviews.Application.Common.Interfaces;
using Reviews.Application.Common.Models;
using Reviews.Application.Movies.Queries.GetMovieRating;
using Reviews.Application.Reviews.Commands.CreateReview;
using Reviews.Domain.Interfaces;

namespace Reviews.Tests;

/// <summary>CachingBehavior (Лаб. 06) у справжньому pipeline: повторний запит — з кешу, команда інвалідує кеш.</summary>
public class CachingBehaviorTests
{
    private readonly IReviewRepository _reviews = Substitute.For<IReviewRepository>();
    private readonly IReviewQueries _queries = Substitute.For<IReviewQueries>();
    private readonly ISender _mediator;

    public CachingBehaviorTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication(new ConfigurationBuilder().Build());   // L2 тут — запасний кеш у пам'яті
        services.AddSingleton(_reviews);
        services.AddSingleton(_queries);
        _mediator = services.BuildServiceProvider().GetRequiredService<ISender>();

        _queries.GetMovieRatingAsync(1, Arg.Any<CancellationToken>())
            .Returns(new MovieRatingDto(1, "Карпатський експрес", 4.5, 2, new Dictionary<int, int> { [4] = 1, [5] = 1 }));
    }

    [Fact]
    public async Task Repeated_query_is_served_from_cache()
    {
        var ct = TestContext.Current.CancellationToken;

        var first = await _mediator.Send(new GetMovieRatingQuery(1), ct);
        var second = await _mediator.Send(new GetMovieRatingQuery(1), ct);

        Assert.Equal(first.Average, second.Average);
        await _queries.Received(1).GetMovieRatingAsync(1, Arg.Any<CancellationToken>());   // MongoDB — лише перший раз
    }

    [Fact]
    public async Task New_review_invalidates_movie_rating()
    {
        var ct = TestContext.Current.CancellationToken;
        await _mediator.Send(new GetMovieRatingQuery(1), ct);

        await _mediator.Send(new CreateReviewCommand(1, "Карпатський експрес", 9, "Іван", "quick", 3), ct);
        await _mediator.Send(new GetMovieRatingQuery(1), ct);

        await _queries.Received(2).GetMovieRatingAsync(1, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Missing_rating_is_not_cached()
    {
        var ct = TestContext.Current.CancellationToken;
        _queries.GetMovieRatingAsync(2, Arg.Any<CancellationToken>()).Returns((MovieRatingDto?)null);

        await Assert.ThrowsAnyAsync<Exception>(() => _mediator.Send(new GetMovieRatingQuery(2), ct));
        await Assert.ThrowsAnyAsync<Exception>(() => _mediator.Send(new GetMovieRatingQuery(2), ct));

        await _queries.Received(2).GetMovieRatingAsync(2, Arg.Any<CancellationToken>());   // 404 не закешовано
    }
}
