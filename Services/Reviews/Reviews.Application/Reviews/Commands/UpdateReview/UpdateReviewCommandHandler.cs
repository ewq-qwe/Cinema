using AutoMapper;
using MediatR;
using MongoDB.Bson;
using Reviews.Application.Common.Models;
using Reviews.Domain.Exceptions;
using Reviews.Domain.Interfaces;
using Reviews.Domain.ValueObjects;

namespace Reviews.Application.Reviews.Commands.UpdateReview;

public sealed class UpdateReviewCommandHandler(IReviewRepository reviews, IMapper mapper)
    : IRequestHandler<UpdateReviewCommand, ReviewDetailsDto>
{
    public async Task<ReviewDetailsDto> Handle(UpdateReviewCommand cmd, CancellationToken ct)
    {
        var id = ObjectId.Parse(cmd.Id);
        var review = await reviews.GetByIdAsync(id, ct) ?? throw NotFoundException.For("Відгук", cmd.Id);

        if (review.Version != cmd.ExpectedVersion)
            throw new ConflictException(
                $"Відгук {cmd.Id} уже змінено (поточна версія {review.Version}, очікувалась {cmd.ExpectedVersion}). Оновіть дані й повторіть.");

        review.Edit(new Rating(cmd.Rating), cmd.Title, cmd.Text, cmd.HasSpoilers,
            cmd.Photos, cmd.Publication, cmd.Pros, cmd.Cons);

        // Репозиторій ще раз перевіряє версію атомарно (filter _id + version) — на випадок гонки між читанням і записом
        await reviews.UpdateAsync(review, ct);
        return mapper.Map<ReviewDetailsDto>(review);
    }
}
