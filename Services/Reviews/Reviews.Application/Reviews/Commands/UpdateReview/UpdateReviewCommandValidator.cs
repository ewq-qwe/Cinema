using FluentValidation;
using Reviews.Application.Common.Validation;
using Reviews.Domain.Entities;
using Reviews.Domain.ValueObjects;

namespace Reviews.Application.Reviews.Commands.UpdateReview;

/// <summary>Загальні обмеження; правила конкретного виду відгуку (він у документі) перевіряє сутність.</summary>
public sealed class UpdateReviewCommandValidator : AbstractValidator<UpdateReviewCommand>
{
    public UpdateReviewCommandValidator()
    {
        RuleFor(x => x.Id).MustBeObjectId();
        RuleFor(x => x.ExpectedVersion).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Rating).InclusiveBetween(Rating.Min, Rating.Max);
        RuleFor(x => x.Title).MaximumLength(Review.TitleMaxLength);
        RuleFor(x => x.Text).MaximumLength(Review.TextMaxLength);
        RuleFor(x => x.Publication).MaximumLength(Review.PublicationMaxLength);
        RuleFor(x => x.Photos).Must(p => p is null || p.Count <= Review.MaxPhotos)
            .WithMessage($"Не більше {Review.MaxPhotos} фото.");
    }
}
