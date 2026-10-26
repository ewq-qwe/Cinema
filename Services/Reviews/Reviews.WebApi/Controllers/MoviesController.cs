using Microsoft.AspNetCore.Mvc;
using Reviews.Application.Common.Models;
using Reviews.Application.Movies.Queries.GetMovieRating;

namespace Reviews.WebApi.Controllers;

/// <summary>Зведені дані про фільм за відгуками.</summary>
[Route("api/movies")]
public sealed class MoviesController : ApiControllerBase
{
    /// <summary>Середня оцінка й розподіл оцінок фільму (aggregation pipeline).</summary>
    [HttpGet("{movieId:int}/rating")]
    [ProducesResponseType<MovieRatingDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MovieRatingDto>> GetRating(int movieId, CancellationToken ct) =>
        Ok(await Mediator.Send(new GetMovieRatingQuery(movieId), ct));
}
