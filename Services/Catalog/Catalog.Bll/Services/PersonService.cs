using AutoMapper;
using Catalog.Bll.Dtos;
using Catalog.Bll.Specifications;
using Catalog.Dal.Abstractions;
using Catalog.Domain.Entities;
using Catalog.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace Catalog.Bll.Services;

public sealed class PersonService(IUnitOfWork uow, IMapper mapper, ILogger<PersonService> logger) : IPersonService
{
    public async Task<PagedResult<PersonDto>> GetListAsync(PersonQuery query, CancellationToken ct = default)
    {
        var (page, pageSize) = Paging.Normalize(query.Page, query.PageSize);
        var spec = new PeopleFilterSpec(query, page, pageSize);   // інша специфікація — той самий репозиторій
        var total = await uow.People.CountAsync(spec, ct);
        var items = await uow.People.ListAsync(spec, ct);
        return new PagedResult<PersonDto>(mapper.Map<IReadOnlyList<PersonDto>>(items), total, page, pageSize);
    }

    public async Task<PersonDto> GetByIdAsync(int id, CancellationToken ct = default) =>
        mapper.Map<PersonDto>(await uow.People.GetByIdAsync(id, ct) ?? throw EntityNotFoundException.For<Person>(id));

    public async Task<PersonDto> CreateAsync(CreatePersonDto input, CancellationToken ct = default)
    {
        var person = new Person(input.FullName, input.BirthYear);
        await uow.People.AddAsync(person, ct);
        await uow.SaveChangesAsync(ct);
        logger.LogInformation("Створено людину {PersonId}", person.Id);
        return mapper.Map<PersonDto>(person);
    }

    /// <summary>Фільмографія — LINQ-запит через проміжну сутність MovieCredit.</summary>
    public async Task<IReadOnlyList<FilmographyItemDto>> GetFilmographyAsync(int id, CancellationToken ct = default)
    {
        _ = await uow.People.GetByIdAsync(id, ct) ?? throw EntityNotFoundException.For<Person>(id);
        return mapper.Map<IReadOnlyList<FilmographyItemDto>>(await uow.MovieCredits.GetFilmographyAsync(id, ct));
    }

    public async Task<IReadOnlyList<PersonDto>> GetLeadActorsAsync(string? genre, int maxBillingOrder, CancellationToken ct = default)
    {
        if (maxBillingOrder is < 1 or > 10)
            throw ValidationException.For("maxBillingOrder", "Значення від 1 до 10.");
        return mapper.Map<IReadOnlyList<PersonDto>>(
            await uow.MovieCredits.GetLeadActorsAsync(string.IsNullOrWhiteSpace(genre) ? null : genre.Trim(), maxBillingOrder, ct));
    }
}
