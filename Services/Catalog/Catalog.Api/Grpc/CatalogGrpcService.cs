using Catalog.Bll.Dtos;
using Catalog.Bll.Services;
using Catalog.Domain.Exceptions;
using Catalog.Grpc;
using Grpc.Core;

namespace Catalog.Api.Grpc;

/// <summary>
/// gRPC-адаптер над тим самим BLL, що й REST-контролери: бізнес-логіка й кеш (Лаб. 06) спільні для обох протоколів.
/// Доменні винятки перетворюються на RpcException з точним кодом (NotFound / InvalidArgument), а не Internal.
/// </summary>
public sealed class CatalogGrpcService(IMovieService movies, IGenreService genres) : CatalogService.CatalogServiceBase
{
    public const int MaxIdsPerRequest = 50;

    public override async Task<MovieReply> GetMovie(GetMovieRequest request, ServerCallContext context)
    {
        if (request.Id <= 0)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Id фільму має бути додатним."));

        try
        {
            return ToReply(await movies.GetByIdAsync(request.Id, context.CancellationToken));
        }
        catch (EntityNotFoundException ex)
        {
            throw new RpcException(new Status(StatusCode.NotFound, ex.Message));
        }
    }

    public override async Task<MoviesReply> GetMoviesByIds(GetMoviesByIdsRequest request, ServerCallContext context)
    {
        var ids = request.Ids.Distinct().ToList();
        if (ids.Count == 0 || ids.Count > MaxIdsPerRequest || ids.Any(id => id <= 0))
            throw new RpcException(new Status(StatusCode.InvalidArgument,
                $"Потрібно від 1 до {MaxIdsPerRequest} додатних Id."));

        var reply = new MoviesReply();
        foreach (var id in ids)   // кожна картка — з кешу, тож послідовне читання дешеве
        {
            try
            {
                reply.Movies.Add(ToReply(await movies.GetByIdAsync(id, context.CancellationToken)));
            }
            catch (EntityNotFoundException)
            {
                reply.MissingIds.Add(id);
            }
        }
        return reply;
    }

    public override async Task<GenresReply> ListGenres(ListGenresRequest request, ServerCallContext context)
    {
        var reply = new GenresReply();
        reply.Genres.AddRange((await genres.GetAllAsync(context.CancellationToken))
            .Select(g => new GenreReply { Id = g.Id, Name = g.Name }));
        return reply;
    }

    private static MovieReply ToReply(MovieDto m)
    {
        var reply = new MovieReply
        {
            Id = m.Id,
            Title = m.Title,
            ReleaseYear = m.ReleaseYear,
            DurationMinutes = m.DurationMinutes,
            AgeRating = m.AgeRating,
            GenreId = m.GenreId,
            GenreName = m.GenreName
        };
        // optional-поля proto3: null у DTO = «не задано» в повідомленні
        if (m.Description is not null) reply.Description = m.Description;
        if (m.Country is not null) reply.Country = m.Country;
        if (m.PosterUrl is not null) reply.PosterUrl = m.PosterUrl;
        if (m.TrailerUrl is not null) reply.TrailerUrl = m.TrailerUrl;

        reply.Credits.AddRange(m.Credits.Select(c =>
        {
            var credit = new CreditReply
            {
                PersonId = c.PersonId,
                PersonFullName = c.PersonFullName,
                Role = c.Role,
                BillingOrder = c.BillingOrder
            };
            if (c.CharacterName is not null) credit.CharacterName = c.CharacterName;
            return credit;
        }));
        return reply;
    }
}
