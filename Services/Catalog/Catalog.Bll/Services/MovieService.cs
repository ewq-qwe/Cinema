using AutoMapper;
using Catalog.Bll.Caching;
using Catalog.Bll.Diagnostics;
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
/// Лаб. 06: читання — cache-aside через дворівневий кеш (L1 пам'ять → L2 Redis → БД);
/// кожен запис синхронно інвалідує картку фільму й усі сторінки списків.
/// </summary>
public sealed class MovieService(IUnitOfWork uow, IMapper mapper, ITwoLevelCache cache, ILogger<MovieService> logger) : IMovieService
{
    public async Task<PagedResult<MovieListItemDto>> GetListAsync(MovieQuery query, CancellationToken ct = default)
    {
        var (page, pageSize) = Paging.Normalize(query.Page, query.PageSize);
        var version = await cache.GetVersionAsync(CatalogCacheKeys.MovieListVersion, ct);
        var key = CatalogCacheKeys.MovieList(version, query, page, pageSize);

        return await cache.GetOrCreateAsync(key, CachePolicy.MovieList, async token =>
        {
            var spec = new MoviesFilterSpec(query, page, pageSize);
            var total = await uow.Movies.CountAsync(spec, token);   // COUNT(*) з тими самими фільтрами — у БД
            var items = await uow.Movies.ListAsync(spec, token);    // ORDER BY … OFFSET … FETCH — у БД
            return new PagedResult<MovieListItemDto>(mapper.Map<IReadOnlyList<MovieListItemDto>>(items), total, page, pageSize);
        }, ct);
    }

    /// <summary>Картка фільму — найчастіше читаний ресурс (REST, gRPC, Aggregator): ключ catalog:movie:{id}.</summary>
    public Task<MovieDto> GetByIdAsync(int id, CancellationToken ct = default) =>
        cache.GetOrCreateAsync(CatalogCacheKeys.Movie(id), CachePolicy.MovieCard,
            async token => mapper.Map<MovieDto>(await LoadAsync(id, token)), ct);

    public async Task<MovieDto> CreateAsync(CreateMovieDto input, CancellationToken ct = default)
    {
        using var activity = CatalogTelemetry.Source.StartActivity("movie.create");
        activity?.SetTag("movie.genre_id", input.GenreId);
        await EnsureGenreExistsAsync(input.GenreId, ct);
        var title = input.Title.Trim();
        if (await uow.Movies.ExistsAsync(title, input.ReleaseYear, ct: ct))
            throw new ConflictException($"Фільм «{title}» ({input.ReleaseYear}) уже є в каталозі.");

        var movie = new Movie(title, input.ReleaseYear, input.DurationMinutes, input.AgeRating, input.GenreId);
        await uow.Movies.AddAsync(movie, ct);
        await uow.SaveChangesAsync(ct);

        activity?.SetTag("movie.id", movie.Id);
        await cache.BumpVersionAsync(CatalogCacheKeys.MovieListVersion, "MovieCreated", ct);
        logger.LogInformation("Створено фільм {MovieId} «{Title}» ({ReleaseYear}), жанр {GenreId}, рейтинг {AgeRating}",
            movie.Id, movie.Title, movie.ReleaseYear, movie.GenreId, movie.AgeRating);
        return await GetByIdAsync(movie.Id, ct);
    }

    public async Task<MovieDto> UpdateAsync(int id, UpdateMovieDto input, CancellationToken ct = default)
    {
        using var activity = CatalogTelemetry.Source.StartActivity("movie.update");
        activity?.SetTag("movie.id", id);
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
        await InvalidateAsync(id, "MovieUpdated", ct);
        logger.LogInformation("Оновлено фільм {MovieId} «{Title}» ({ReleaseYear}): {DurationMinutes} хв, рейтинг {AgeRating}, жанр {GenreId}",
            id, movie.Title, movie.ReleaseYear, movie.DurationMinutes, movie.AgeRating, movie.GenreId);
        return await GetByIdAsync(id, ct);
    }

    public async Task<MovieDto> SetDetailAsync(int id, SetMovieDetailDto input, CancellationToken ct = default)
    {
        using var activity = CatalogTelemetry.Source.StartActivity("movie.set-detail");
        activity?.SetTag("movie.id", id);
        var movie = await LoadAsync(id, ct);
        movie.SetDetail(input.Description, input.Country, input.TrailerUrl, input.PosterUrl, input.Budget);
        await uow.SaveChangesAsync(ct);
        await InvalidateAsync(id, "MovieDetailChanged", ct);
        logger.LogInformation("Фільм {MovieId}: оновлено опис, країна {Country}", id, input.Country);
        return mapper.Map<MovieDto>(movie);
    }

    public async Task<MovieDto> AddCreditAsync(int id, AddCreditDto input, CancellationToken ct = default)
    {
        using var activity = CatalogTelemetry.Source.StartActivity("movie.add-credit");
        activity?.SetTag("movie.id", id);
        activity?.SetTag("person.id", input.PersonId);
        activity?.SetTag("credit.role", input.Role);
        var movie = await LoadAsync(id, ct);
        var person = await uow.People.GetByIdAsync(input.PersonId, ct)
                     ?? throw ValidationException.For("personId", $"Людини {input.PersonId} немає в каталозі.");

        // Правила титрів (без дублів, персонаж лише в актора, унікальна позиція) — у самій сутності
        movie.AddCredit(person, Enum.Parse<CreditRole>(input.Role, ignoreCase: true), input.CharacterName, input.BillingOrder);
        await uow.SaveChangesAsync(ct);
        await InvalidateAsync(id, "CreditAdded", ct);

        logger.LogInformation("Фільм {MovieId}: додано {PersonId} як {Role}, позиція в титрах {BillingOrder}",
            id, person.Id, input.Role, input.BillingOrder);
        return mapper.Map<MovieDto>(movie);
    }

    public async Task RemoveCreditAsync(int id, int personId, string role, CancellationToken ct = default)
    {
        if (!Enum.TryParse<CreditRole>(role, ignoreCase: true, out var creditRole) || !Enum.IsDefined(creditRole))
            throw ValidationException.For("role", "Роль: Actor або Director.");

        using var activity = CatalogTelemetry.Source.StartActivity("movie.remove-credit");
        activity?.SetTag("movie.id", id);
        var movie = await LoadAsync(id, ct);
        movie.RemoveCredit(personId, creditRole);
        await uow.SaveChangesAsync(ct);
        await InvalidateAsync(id, "CreditRemoved", ct);
        logger.LogInformation("Фільм {MovieId}: прибрано {PersonId} з ролі {Role}", id, personId, creditRole);
    }

    public async Task ArchiveAsync(int id, CancellationToken ct = default)
    {
        using var activity = CatalogTelemetry.Source.StartActivity("movie.archive");
        activity?.SetTag("movie.id", id);
        var movie = await uow.Movies.GetByIdAsync(id, ct) ?? throw EntityNotFoundException.For<Movie>(id);
        movie.Archive();
        await uow.SaveChangesAsync(ct);
        await InvalidateAsync(id, "MovieArchived", ct);
        logger.LogInformation("Фільм {MovieId} «{Title}» перенесено в архів, статус {Status}", id, movie.Title, "Archived");
    }

    /// <summary>Інвалідація після запису: картка фільму й усі сторінки списків (нова версія).</summary>
    private async Task InvalidateAsync(int id, string reason, CancellationToken ct)
    {
        await cache.RemoveAsync(CatalogCacheKeys.Movie(id), reason, ct);
        await cache.BumpVersionAsync(CatalogCacheKeys.MovieListVersion, reason, ct);
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
