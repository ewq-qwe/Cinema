using FluentValidation;
using MediatR;
using Platform.EventBus.Abstractions;
using Platform.EventBus.Consuming;
using Platform.EventBus.Events;
using Reviews.Application.Movies.Commands.SyncMovieTitle;

namespace Reviews.WebApi.IntegrationEvents;

/// <summary>
/// Лаб. 08: фільм перейменовано в Каталозі → movieTitle в усіх відгуках і обговореннях фільму.
/// Адаптер: перетворює integration event на команду MediatR (бізнес-логіка й ідемпотентність — у Application),
/// тож ця обробка проходить той самий pipeline (Logging → Validation → Performance), що й HTTP-команди.
/// </summary>
public sealed class MovieRenamedEventHandler(ISender mediator, MessageContext message, ILogger<MovieRenamedEventHandler> logger)
    : IIntegrationEventHandler<MovieRenamedEvent>
{
    public async Task HandleAsync(MovieRenamedEvent @event, CancellationToken ct)
    {
        SyncMovieTitleOutcome outcome;
        try
        {
            outcome = await mediator.Send(new SyncMovieTitleCommand(@event.EventId, @event.MovieId, @event.NewTitle, @event.OccurredAt), ct);
        }
        catch (ValidationException ex)
        {
            // Некоректні дані події (порожня назва, MovieId ≤ 0): повтор не допоможе → одразу DLQ
            throw new NonRetryableEventException($"MovieRenamedEvent {@event.EventId} для фільму {@event.MovieId} не пройшов валідацію: {ex.Message}", ex);
        }

        if (outcome == SyncMovieTitleOutcome.Duplicate)
            message.MarkDuplicate();
        logger.LogDebug("MovieRenamedEvent {EventId} for movie {MovieId}: {Outcome}", @event.EventId, @event.MovieId, outcome);
    }
}
