using FluentValidation;
using Reviews.Application.Common.Validation;
using Reviews.Domain.Entities;
using Reviews.Domain.Interfaces;
using Reviews.Domain.ValueObjects;

namespace Reviews.Application.Reviews.Commands.CreateReview;

public sealed class CreateReviewCommandValidator : AbstractValidator<CreateReviewCommand>
{
    public CreateReviewCommandValidator(IReviewRepository reviews)
    {
        RuleFor(x => x.MovieId).GreaterThan(0);
        RuleFor(x => x.MovieTitle).NotEmpty().MaximumLength(200);
        RuleFor(x => x.AuthorId).GreaterThan(0);
        RuleFor(x => x.AuthorName).NotEmpty().MaximumLength(Author.DisplayNameMaxLength);
        RuleFor(x => x.Kind).NotEmpty().MustBeReviewKind();
        RuleFor(x => x.Rating).InclusiveBetween(Rating.Min, Rating.Max);

        // Правила кожного виду відгуку (гнучка схема)
        When(x => x.Kind == "quick", () =>
        {
            RuleFor(x => x.Title).Empty().WithMessage("Швидкий відгук — лише оцінка, без заголовка.");
            RuleFor(x => x.Text).Empty().WithMessage("Швидкий відгук — лише оцінка, без тексту.");
        });
        When(x => x.Kind == "full", () =>
        {
            RuleFor(x => x.Text).NotEmpty().MaximumLength(Review.TextMaxLength);
            RuleFor(x => x.Title).MaximumLength(Review.TitleMaxLength);
            RuleFor(x => x.Photos).Must(p => p is null || p.Count <= Review.MaxPhotos)
                .WithMessage($"Не більше {Review.MaxPhotos} фото.");
            RuleForEach(x => x.Photos).Must(BeHttpUrl).WithMessage("Фото має бути http(s)-посиланням.");
        });
        When(x => x.Kind == "critic", () =>
        {
            RuleFor(x => x.Text).NotEmpty().MaximumLength(Review.TextMaxLength);
            RuleFor(x => x.Publication).NotEmpty().MaximumLength(Review.PublicationMaxLength);
            RuleForEach(x => x.Pros).NotEmpty().MaximumLength(Review.PointMaxLength);
            RuleForEach(x => x.Cons).NotEmpty().MaximumLength(Review.PointMaxLength);
        });

        // Асинхронне правило із запитом до MongoDB: один глядач — один відгук на фільм
        RuleFor(x => x)
            .MustAsync(async (cmd, ct) => !await reviews.ExistsAsync(cmd.MovieId, cmd.AuthorId, ct))
            .WithName("AuthorId")
            .WithMessage("Цей глядач уже залишив відгук на цей фільм.")
            .When(x => x.MovieId > 0 && x.AuthorId > 0);
    }

    private static bool BeHttpUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";
}
