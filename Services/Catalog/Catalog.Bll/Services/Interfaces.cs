using Catalog.Bll.Dtos;

namespace Catalog.Bll.Services;

public interface IMovieService
{
    Task<PagedResult<MovieListItemDto>> GetListAsync(MovieQuery query, CancellationToken ct = default);
    Task<MovieDto> GetByIdAsync(int id, CancellationToken ct = default);
    Task<MovieDto> CreateAsync(CreateMovieDto input, CancellationToken ct = default);
    Task<MovieDto> UpdateAsync(int id, UpdateMovieDto input, CancellationToken ct = default);
    Task<MovieDto> SetDetailAsync(int id, SetMovieDetailDto input, CancellationToken ct = default);
    Task<MovieDto> AddCreditAsync(int id, AddCreditDto input, CancellationToken ct = default);
    Task RemoveCreditAsync(int id, int personId, string role, CancellationToken ct = default);
    Task ArchiveAsync(int id, CancellationToken ct = default);
}

public interface IGenreService
{
    Task<IReadOnlyList<GenreDto>> GetAllAsync(CancellationToken ct = default);
    Task<GenreWithMoviesDto> GetWithMoviesAsync(int id, CancellationToken ct = default);
    Task<GenreDto> CreateAsync(CreateGenreDto input, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}

public interface IPersonService
{
    Task<PagedResult<PersonDto>> GetListAsync(PersonQuery query, CancellationToken ct = default);
    Task<PersonDto> GetByIdAsync(int id, CancellationToken ct = default);
    Task<PersonDto> CreateAsync(CreatePersonDto input, CancellationToken ct = default);
    Task<IReadOnlyList<FilmographyItemDto>> GetFilmographyAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<PersonDto>> GetLeadActorsAsync(string? genre, int maxBillingOrder, CancellationToken ct = default);
}
