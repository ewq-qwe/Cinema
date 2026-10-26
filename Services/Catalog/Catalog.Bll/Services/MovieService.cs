using AutoMapper;
using Catalog.Bll.Dtos;
using Catalog.Bll.Specifications;
using Catalog.Dal.Abstractions;
using Catalog.Domain.Entities;
using Catalog.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace Catalog.Bll.Services;

/// <summary>
/// Бізнес-операції з фільмами. Сервіс не працює з EF напряму — лише через репозиторії й Unit of Work.
/// Правила самої сутності (рік, тривалість, титри) перевіряє Movie; сервіс перевіряє правила,
/// для яких потрібні інші дані з БД: існування жанру й людини, унікальність «назва + рік».
/// </summary>
public sealed class MovieService(IUnitOfWork uow, IMapper mapper, ILogger<MovieService> logger) : IMovieService
{
    public async Task<PagedResult<MovieListItemDto>> GetListAsync(MovieQuery query, CancellationToken ct = default)
    {
        var (page, pageSize) = Paging.Normalize(query.Page, query.PageSize);
        var spec = new MoviesFilterSpec(query, page, pageSize);

        var total = await uow.Movies.CountAsync(spec, ct);   // COUNT(*) з тими самими фільтрами — у БД
        var items = await uow.Movies.ListAsync(spec, ct);    // ORDER BY … OFFSET … FETCH — у БД
        return new PagedResult<MovieListItemDto>(mapper.Map<IReadOnlyList<MovieListItemDto>>(items), total, page, pageSize);
    }

    public async Task<MovieDto> GetByIdAsync(int id, CancellationToken ct = default) =>
        mapper.Map<MovieDto>(await LoadAsync(id, ct));

    public async Task<MovieDto> CreateAsync(CreateMovieDto input, CancellationToken ct = default)
    {
        await EnsureGenreExistsAsync(input.GenreId, ct);
        var title = input.Title.Trim();
        if (await uow.Movies.ExistsAsync(title, input.ReleaseYear, ct: ct))
            throw new ConflictException($"Фільм «{title}» ({input.ReleaseYear}) уже є в каталозі.");

        var movie = new Movie(title, input.ReleaseYear, input.DurationMinutes, input.AgeRating, input.GenreId);
        await uow.Movies.AddAsync(movie, ct);
        await uow.SaveChangesAsync(ct);

        logger.LogInformation("Створено фільм {MovieId} «{Title}»", movie.Id, movie.Title);
        return await GetByIdAsync(movie.Id, ct);
    }

    public async Task<MovieDto> UpdateAsync(int id, UpdateMovieDto input, CancellationToken ct = default)
    {
        var movie = await LoadAsync(id, ct);
        uow.Movies.SetExpectedRowVersion(movie, Convert.FromBase64String(input.RowVersion));

        if (movie.GenreId != input.GenreId)
            await EnsureGenreExistsAsync(input.GenreId, ct);
        var title = input.Title.Trim();
        if (await uow.Movies.ExistsAsync(title, input.ReleaseYear, exceptId: id, ct))
            throw new ConflictException($"Фільм «{title}» ({input.ReleaseYear}) уже є в каталозі.");

        movie.SetTitleAndYear(title, input.ReleaseYear);
        movie.ChangeDuration(input.DurationMinutes);
        movie.ChangeAgeRating(input.AgeRating);
        movie.MoveToGenre(input.GenreId);

        await uow.SaveChangesAsync(ct);   // інша RowVersion у БД → ConflictException (409)
        return await GetByIdAsync(id, ct);
    }

    public async Task<MovieDto> SetDetailAsync(int id, SetMovieDetailDto input, CancellationToken ct = default)
    {
        var movie = await LoadAsync(id, ct);
        movie.SetDetail(input.Description, input.Country, input.TrailerUrl, input.PosterUrl, input.Budget);
        await uow.SaveChangesAsync(ct);
        return mapper.Map<MovieDto>(movie);
    }

    public async Task<MovieDto> AddCreditAsync(int id, AddCreditDto input, CancellationToken ct = default)
    {
        var movie = await LoadAsync(id, ct);
        var person = await uow.People.GetByIdAsync(input.PersonId, ct)
                     ?? throw ValidationException.For("personId", $"Людини {input.PersonId} немає в каталозі.");

        // Правила титрів (без дублів, персонаж лише в актора, унікальна позиція) — у самій сутності
        movie.AddCredit(person, Enum.Parse<CreditRole>(input.Role, ignoreCase: true), input.CharacterName, input.BillingOrder);
        await uow.SaveChangesAsync(ct);

        logger.LogInformation("Фільм {MovieId}: додано {PersonId} як {Role}", id, person.Id, input.Role);
        return mapper.Map<MovieDto>(movie);
    }

    public async Task RemoveCreditAsync(int id, int personId, string role, CancellationToken ct = default)
    {
        if (!Enum.TryParse<CreditRole>(role, ignoreCase: true, out var creditRole) || !Enum.IsDefined(creditRole))
            throw ValidationException.For("role", "Роль: Actor або Director.");

        var movie = await LoadAsync(id, ct);
        movie.RemoveCredit(personId, creditRole);
        await uow.SaveChangesAsync(ct);
    }

    public async Task ArchiveAsync(int id, CancellationToken ct = default)
    {
        var movie = await uow.Movies.GetByIdAsync(id, ct) ?? throw EntityNotFoundException.For<Movie>(id);
        movie.Archive();
        await uow.SaveChangesAsync(ct);
        logger.LogInformation("Фільм {MovieId} перенесено в архів", id);
    }

    /// <summary>Картка фільму (eager loading) з відстеженням змін — для читання й подальших операцій.</summary>
    private async Task<Movie> LoadAsync(int id, CancellationToken ct) =>
        await uow.Movies.GetWithDetailsAsync(id, ct) ?? throw EntityNotFoundException.For<Movie>(id);

    private async Task EnsureGenreExistsAsync(int genreId, CancellationToken ct)
    {
        if (await uow.Genres.GetByIdAsync(genreId, ct) is null)
            throw ValidationException.For("genreId", $"Жанру {genreId} не існує.");
    }
}
