using MediatR;
using Microsoft.Extensions.Logging;
using Reviews.Application.Common.Caching;
using Reviews.Application.Common.Diagnostics;
using Reviews.Application.Common.Validation;
using Reviews.Domain.Entities;
using Reviews.Domain.Interfaces;
using Reviews.Domain.ValueObjects;

namespace Reviews.Application.Reviews.Commands.CreateReview;

public sealed class CreateReviewCommandHandler(IReviewRepository reviews, ITwoLevelCache cache, ILogger<CreateReviewCommandHandler> logger)
    : IRequestHandler<CreateReviewCommand, string>
{
    public async Task<string> Handle(CreateReviewCommand cmd, CancellationToken ct)
    {
        using var activity = ReviewsTelemetry.Source.StartActivity("review.create");
        activity?.SetTag("movie.id", cmd.MovieId);
        activity?.SetTag("review.kind", cmd.Kind);

        var review = new Review(
            cmd.MovieId, cmd.MovieTitle,
            new Author(cmd.AuthorId, cmd.AuthorName),
            ReviewKinds.Parse(cmd.Kind),
            new Rating(cmd.Rating),
            cmd.Title, cmd.Text, cmd.HasSpoilers, cmd.Photos, cmd.Publication, cmd.Pros, cmd.Cons);

        // Гонку двох однакових запитів зупинить унікальний індекс (movieId, author.customerId) → 409
        await reviews.AddAsync(review, ct);

        // Інвалідація синхронно із записом: рейтинг фільму й усі сторінки списків
        await cache.RemoveAsync(ReviewsCacheKeys.MovieRating(review.MovieId), "ReviewCreated", ct);
        await cache.BumpVersionAsync(ReviewsCacheKeys.ListVersion, "ReviewCreated", ct);

        activity?.SetTag("review.id", review.Id.ToString());
        logger.LogInformation("Створено відгук {ReviewId} на фільм {MovieId}: вид {Kind}, оцінка {Rating}, глядач {CustomerId}",
            review.Id.ToString(), review.MovieId, cmd.Kind, review.Rating.Value, review.Author.CustomerId);
        return review.Id.ToString();
    }
}
