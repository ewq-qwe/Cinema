using AutoMapper;
using Catalog.Bll.Dtos;
using Catalog.Dal.Abstractions;
using Catalog.Domain.Entities;
using Catalog.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace Catalog.Bll.Services;

public sealed class GenreService(IUnitOfWork uow, IMapper mapper, ILogger<GenreService> logger) : IGenreService
{
    public async Task<IReadOnlyList<GenreDto>> GetAllAsync(CancellationToken ct = default) =>
        mapper.Map<IReadOnlyList<GenreDto>>((await uow.Genres.GetAllAsync(ct)).OrderBy(g => g.Name).ToList());

    /// <summary>Жанр і його фільми — explicit loading (два запити).</summary>
    public async Task<GenreWithMoviesDto> GetWithMoviesAsync(int id, CancellationToken ct = default) =>
        mapper.Map<GenreWithMoviesDto>(await uow.Genres.GetWithMoviesAsync(id, ct) ?? throw EntityNotFoundException.For<Genre>(id));

    public async Task<GenreDto> CreateAsync(CreateGenreDto input, CancellationToken ct = default)
    {
        var name = input.Name.Trim();
        if (await uow.Genres.ExistsByNameAsync(name, ct))
            throw new ConflictException($"Жанр «{name}» уже існує.");

        var genre = new Genre(name);
        await uow.Genres.AddAsync(genre, ct);
        await uow.SaveChangesAsync(ct);
        logger.LogInformation("Створено жанр {GenreId} «{Name}»", genre.Id, genre.Name);
        return mapper.Map<GenreDto>(genre);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var genre = await uow.Genres.GetByIdAsync(id, ct) ?? throw EntityNotFoundException.For<Genre>(id);
        if (await uow.Genres.HasMoviesAsync(id, ct))
            throw new ConflictException($"У жанрі «{genre.Name}» є фільми — видалення неможливе.");

        uow.Genres.Delete(genre);
        await uow.SaveChangesAsync(ct);
    }
}
