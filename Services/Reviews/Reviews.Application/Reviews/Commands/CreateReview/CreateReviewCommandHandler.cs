using MediatR;
using Reviews.Application.Common.Validation;
using Reviews.Domain.Entities;
using Reviews.Domain.Interfaces;
using Reviews.Domain.ValueObjects;

namespace Reviews.Application.Reviews.Commands.CreateReview;

public sealed class CreateReviewCommandHandler(IReviewRepository reviews) : IRequestHandler<CreateReviewCommand, string>
{
    public async Task<string> Handle(CreateReviewCommand cmd, CancellationToken ct)
    {
        var review = new Review(
            cmd.MovieId, cmd.MovieTitle,
            new Author(cmd.AuthorId, cmd.AuthorName),
            ReviewKinds.Parse(cmd.Kind),
            new Rating(cmd.Rating),
            cmd.Title, cmd.Text, cmd.HasSpoilers, cmd.Photos, cmd.Publication, cmd.Pros, cmd.Cons);

        // Гонку двох однакових запитів зупинить унікальний індекс (movieId, author.customerId) → 409
        await reviews.AddAsync(review, ct);
        return review.Id.ToString();
    }
}
