using FluentValidation;
using Reviews.Application.Common.Interfaces;
using Reviews.Application.Common.Validation;
using Reviews.Domain.ValueObjects;

namespace Reviews.Application.Reviews.Queries.GetReviewsList;

public sealed class GetReviewsListQueryValidator : AbstractValidator<GetReviewsListQuery>
{
    public GetReviewsListQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, GetReviewsListQueryHandler.MaxPageSize);
        RuleFor(x => x.MovieId).GreaterThan(0).When(x => x.MovieId.HasValue);
        RuleFor(x => x.AuthorId).GreaterThan(0).When(x => x.AuthorId.HasValue);
        RuleFor(x => x.Kind).MustBeReviewKind().When(x => x.Kind is not null);
        RuleFor(x => x.MinRating).InclusiveBetween(Rating.Min, Rating.Max).When(x => x.MinRating.HasValue);
        RuleFor(x => x.Search).MaximumLength(100);
        RuleFor(x => x.SortBy).Must(s => ReviewSort.All.Contains(s))
            .WithMessage($"'{{PropertyName}}' має бути одним із: {string.Join(", ", ReviewSort.All)}.");
    }
}
