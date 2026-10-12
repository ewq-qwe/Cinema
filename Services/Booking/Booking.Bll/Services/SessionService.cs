using AutoMapper;
using Booking.Bll.Dtos;
using Booking.Dal.Abstractions;
using Booking.Domain;
using Booking.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace Booking.Bll.Services;

public sealed class SessionService(IUnitOfWork uow, IMapper mapper, ILogger<SessionService> logger) : ISessionService
{
    public async Task<SessionDto> GetAsync(long id, CancellationToken ct = default)
    {
        var session = await uow.Sessions.GetWithMovieAsync(id, ct)
                      ?? throw new NotFoundException($"Сеанс {id} не знайдено.");
        return mapper.Map<SessionDto>(session);
    }

    public async Task<IReadOnlyList<SessionDto>> GetUpcomingAsync(long? movieId, CancellationToken ct = default) =>
        mapper.Map<IReadOnlyList<SessionDto>>(await uow.Sessions.GetUpcomingAsync(movieId, ct));

    public async Task<IReadOnlyList<SeatDto>> GetSeatMapAsync(long id, CancellationToken ct = default)
    {
        _ = await uow.Sessions.GetByIdAsync(id, ct) ?? throw new NotFoundException($"Сеанс {id} не знайдено.");
        return mapper.Map<IReadOnlyList<SeatDto>>(await uow.Sessions.GetSeatMapAsync(id, ct));
    }

    public async Task<SessionDto> CreateAsync(SaveSessionDto input, CancellationToken ct = default)
    {
        await ValidateAsync(input, ct);

        // Зал+час унікальні (UNIQUE у БД): накладка сеансів → BusinessConflictException (409)
        var id = await uow.Sessions.AddAsync(ToEntity(input), ct);
        logger.LogInformation("Створено сеанс {SessionId}", id);
        return await GetAsync(id, ct);
    }

    public async Task<SessionDto> UpdateAsync(long id, SaveSessionDto input, CancellationToken ct = default)
    {
        var existing = await uow.Sessions.GetByIdAsync(id, ct) ?? throw new NotFoundException($"Сеанс {id} не знайдено.");
        await ValidateAsync(input, ct);

        // На сеанс уже продано квитки — фільм, зал і час змінювати не можна (квитки містять знімок часу)
        var scheduleChanged = existing.MovieId != input.MovieId || existing.HallId != input.HallId
                              || existing.StartsAt != input.StartsAt;
        if (scheduleChanged && await uow.Sessions.HasActiveTicketsAsync(id, ct))
            throw new BusinessConflictException("На сеанс уже продано квитки — змінювати фільм, зал або час не можна.");

        var entity = ToEntity(input);
        entity.Id = id;
        entity.UpdatedAt = DateTime.UtcNow;
        await uow.Sessions.UpdateAsync(entity, ct);
        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        // Сеанс з бронюваннями видалити не дасть FK у БД → BusinessConflictException (409)
        if (!await uow.Sessions.DeleteAsync(id, ct))
            throw new NotFoundException($"Сеанс {id} не знайдено.");
        logger.LogInformation("Видалено сеанс {SessionId}", id);
    }

    /// <summary>Бізнес-валідація зі станом БД: чи існують фільм і зал, чи час у майбутньому.</summary>
    private async Task ValidateAsync(SaveSessionDto input, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (input.StartsAt <= DateTime.UtcNow)
            errors["startsAt"] = ["Сеанс можна запланувати лише на майбутнє."];
        if (!await uow.Sessions.MovieExistsAsync(input.MovieId, ct))
            errors["movieId"] = [$"Фільм {input.MovieId} відсутній у локальній копії довідника фільмів."];
        if (!await uow.Sessions.HallExistsAsync(input.HallId, ct))
            errors["hallId"] = [$"Зал {input.HallId} не існує або неактивний."];

        if (errors.Count > 0)
            throw new ValidationException("Дані сеансу некоректні.", errors);
    }

    private static Session ToEntity(SaveSessionDto input) => new()
    {
        MovieId = input.MovieId,
        HallId = input.HallId,
        StartsAt = input.StartsAt!.Value,
        BasePrice = input.BasePrice
    };
}
