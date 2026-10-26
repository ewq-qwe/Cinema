using FluentValidation;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using NSubstitute;
using Reviews.Application;
using Reviews.Application.Common.Interfaces;
using Reviews.Application.Reviews.Commands.CreateReview;
using Reviews.Application.Reviews.Queries.GetReviewById;
using Reviews.Application.Reviews.Queries.GetReviewsList;
using Reviews.Domain.Entities;
using Reviews.Domain.Interfaces;

namespace Reviews.Tests;

/// <summary>Справжній pipeline MediatR (AddApplication) з підмінними репозиторіями: валідація спрацьовує ДО обробника.</summary>
public class PipelineTests
{
    private readonly IReviewRepository _reviews = Substitute.For<IReviewRepository>();
    private readonly IReviewQueries _queries = Substitute.For<IReviewQueries>();
    private readonly ISender _mediator;

    public PipelineTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication(new ConfigurationBuilder().Build());
        services.AddSingleton(_reviews);
        services.AddSingleton(_queries);
        _mediator = services.BuildServiceProvider().GetRequiredService<ISender>();
    }

    private static CreateReviewCommand ValidCommand(int rating = 5) =>
        new(1, "Карпатський експрес", 7, "Марія", "full", rating, "Чудово", "Дуже сподобалось");

    [Fact]
    public async Task Command_with_rating_10_is_rejected_before_handler()
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(() => _mediator.Send(ValidCommand(rating: 10)));

        Assert.Contains(ex.Errors, e => e.PropertyName == nameof(CreateReviewCommand.Rating));
        await _reviews.DidNotReceive().AddAsync(Arg.Any<Review>(), Arg.Any<CancellationToken>());   // обробник не викликався
    }

    [Fact]
    public async Task All_validation_errors_are_collected_together()
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _mediator.Send(new CreateReviewCommand(0, "", 0, "", "epic", 10)));

        var fields = ex.Errors.Select(e => e.PropertyName).Distinct().ToList();
        Assert.Superset(new HashSet<string> { "MovieId", "MovieTitle", "AuthorId", "AuthorName", "Kind", "Rating" }, fields.ToHashSet());
    }

    [Fact]
    public async Task Async_rule_queries_repository_and_rejects_second_review_of_same_movie()
    {
        _reviews.ExistsAsync(1, 7, Arg.Any<CancellationToken>()).Returns(true);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => _mediator.Send(ValidCommand()));

        Assert.Contains(ex.Errors, e => e.ErrorMessage.Contains("уже залишив відгук"));
        await _reviews.DidNotReceive().AddAsync(Arg.Any<Review>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Valid_command_reaches_handler_and_returns_ObjectId_string()
    {
        var id = await _mediator.Send(ValidCommand());

        Assert.True(ObjectId.TryParse(id, out _));
        await _reviews.Received(1).AddAsync(Arg.Is<Review>(r => r.Rating.Value == 5 && r.Author.CustomerId == 7), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("123")]
    [InlineData("not-an-object-id")]
    [InlineData("000000000000000000000000")]
    public async Task Query_with_invalid_ObjectId_is_rejected(string id)
    {
        await Assert.ThrowsAsync<ValidationException>(() => _mediator.Send(new GetReviewByIdQuery(id)));
        await _queries.DidNotReceiveWithAnyArgs().GetByIdAsync(default, default);
    }

    [Fact]
    public async Task List_query_validates_paging_and_sort()
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _mediator.Send(new GetReviewsListQuery(SortBy: "random", Page: 0, PageSize: 500)));
        Assert.Equal(3, ex.Errors.Count());
    }
}
