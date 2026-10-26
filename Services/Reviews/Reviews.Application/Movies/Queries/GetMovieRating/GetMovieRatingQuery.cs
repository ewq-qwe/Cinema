using FluentValidation;
using MediatR;
using Reviews.Application.Common.Interfaces;
using Reviews.Application.Common.Messaging;
using Reviews.Application.Common.Models;
using Reviews.Domain.Exceptions;

namespace Reviews.Application.Movies.Queries.GetMovieRating;

/// <summary>Середня оцінка й розподіл оцінок фільму (aggregation pipeline в Infrastructure).</summary>
public sealed record GetMovieRatingQuery(int MovieId) : IQuery<MovieRatingDto>;

public sealed class GetMovieRatingQueryHandler(IReviewQueries queries) : IRequestHandler<GetMovieRatingQuery, MovieRatingDto>
{
    public async Task<MovieRatingDto> Handle(GetMovieRatingQuery q, CancellationToken ct) =>
        await queries.GetMovieRatingAsync(q.MovieId, ct)
        ?? throw new NotFoundException($"На фільм {q.MovieId} ще немає відгуків.");
}

public sealed class GetMovieRatingQueryValidator : AbstractValidator<GetMovieRatingQuery>
{
    public GetMovieRatingQueryValidator() => RuleFor(x => x.MovieId).GreaterThan(0);
}
