using Booking.Dal.Abstractions;
using Booking.Domain.Exceptions;
using Platform.EventBus.Abstractions;
using Platform.EventBus.Consuming;
using Platform.EventBus.Events;

namespace Booking.Api.IntegrationEvents;

/// <summary>
/// Спільний каркас ідемпотентного обробника: одна транзакція Unit of Work на повідомлення —
/// INSERT EventId у dbo.ProcessedEvents і зміна копії dbo.Movies фіксуються РАЗОМ (або разом відкочуються).
/// Якщо EventId уже є — подія вже оброблена (повторна доставка at-least-once): пропуск без змін.
/// </summary>
public abstract class MovieCopyHandler<TEvent>(IUnitOfWork uow, MessageContext message, ILogger logger)
    : IIntegrationEventHandler<TEvent> where TEvent : IntegrationEvent
{
    private static readonly string[] AgeRatings = ["0+", "6+", "12+", "16+", "18+"];

    public async Task HandleAsync(TEvent @event, CancellationToken ct)
    {
        Validate(@event);   // некоректні дані не виправить жоден повтор → одразу DLQ

        await uow.BeginTransactionAsync(ct: ct);
        try
        {
            if (!await uow.ProcessedEvents.TryAddAsync(@event.EventId, typeof(TEvent).Name, ct))
            {
                await uow.RollbackAsync(ct);
                message.MarkDuplicate();
                logger.LogInformation("Duplicate event {EventType} {EventId} for movie {MovieId} skipped (already processed)",
                    typeof(TEvent).Name, @event.EventId, MovieId(@event));
                return;
            }

            var applied = await ApplyAsync(@event, ct);
            await uow.CommitAsync(ct);

            if (applied)
                logger.LogInformation("Movie copy {MovieId} updated by {EventType} {EventId}: {Change}",
                    MovieId(@event), typeof(TEvent).Name, @event.EventId, Describe(@event));
            else
                logger.LogWarning("Movie copy {MovieId}: {EventType} {EventId} ({OccurredAt:O}) is stale or the movie is unknown — skipped",
                    MovieId(@event), typeof(TEvent).Name, @event.EventId, @event.OccurredAt);
        }
        catch (BusinessConflictException ex)
        {
            // Порушення CHECK / FK у БД — дані події несумісні з копією: повтор не допоможе
            await uow.RollbackAsync(ct);
            throw new NonRetryableEventException($"Подію {@event.EventId} відхилено обмеженнями БД: {ex.Message}", ex);
        }
        catch
        {
            await uow.RollbackAsync(ct);   // тимчасовий збій (БД недоступна) — базовий consumer повторить
            throw;
        }
    }

    /// <summary>Бізнес-дія. false — нічого не змінено (застаріла подія).</summary>
    protected abstract Task<bool> ApplyAsync(TEvent @event, CancellationToken ct);

    protected abstract int MovieId(TEvent @event);

    protected abstract string Describe(TEvent @event);

    protected virtual void Validate(TEvent @event) { }

    protected static void ValidateSnapshot(int movieId, string title, int duration, string ageRating)
    {
        if (movieId <= 0) throw new NonRetryableEventException($"Некоректний MovieId {movieId}");
        if (string.IsNullOrWhiteSpace(title) || title.Length > 200) throw new NonRetryableEventException($"Некоректна назва фільму {movieId}");
        if (duration is < 1 or > 600) throw new NonRetryableEventException($"Некоректна тривалість {duration} хв фільму {movieId}");
        if (!AgeRatings.Contains(ageRating)) throw new NonRetryableEventException($"Некоректний віковий рейтинг «{ageRating}» фільму {movieId}");
    }
}

/// <summary>Новий фільм у Каталозі → новий рядок у копії Movies (на нього можна створювати сеанси).</summary>
public sealed class MovieCreatedHandler(IUnitOfWork uow, MessageContext message, ILogger<MovieCreatedHandler> logger)
    : MovieCopyHandler<MovieCreatedEvent>(uow, message, logger)
{
    private readonly IUnitOfWork _uow = uow;

    protected override void Validate(MovieCreatedEvent e) => ValidateSnapshot(e.MovieId, e.Title, e.DurationMinutes, e.AgeRating);

    protected override Task<bool> ApplyAsync(MovieCreatedEvent e, CancellationToken ct) =>
        _uow.Movies.UpsertAsync(new MovieSnapshot(e.MovieId, e.Title, e.DurationMinutes, e.AgeRating), e.OccurredAt, ct);

    protected override int MovieId(MovieCreatedEvent e) => e.MovieId;

    protected override string Describe(MovieCreatedEvent e) => $"created «{e.Title}», {e.DurationMinutes} хв, {e.AgeRating}";
}

/// <summary>
/// Перейменування → Movies.Title. Bookings.MovieTitle НЕ змінюється: за політикою з Лаб. 01 це знімок на момент
/// бронювання (те, що надруковано на квитку), як OrderItems.UnitPrice в eShop.
/// </summary>
public sealed class MovieRenamedHandler(IUnitOfWork uow, MessageContext message, ILogger<MovieRenamedHandler> logger)
    : MovieCopyHandler<MovieRenamedEvent>(uow, message, logger)
{
    private readonly IUnitOfWork _uow = uow;

    protected override void Validate(MovieRenamedEvent e) => ValidateSnapshot(e.MovieId, e.NewTitle, e.DurationMinutes, e.AgeRating);

    protected override Task<bool> ApplyAsync(MovieRenamedEvent e, CancellationToken ct) =>
        _uow.Movies.UpsertAsync(new MovieSnapshot(e.MovieId, e.NewTitle, e.DurationMinutes, e.AgeRating), e.OccurredAt, ct);

    protected override int MovieId(MovieRenamedEvent e) => e.MovieId;

    protected override string Describe(MovieRenamedEvent e) => $"title «{e.OldTitle}» → «{e.NewTitle}»";
}

/// <summary>Тривалість / віковий рейтинг → копія Movies. Ціни квитків (Tickets.Price) і вже створені бронювання не змінюються.</summary>
public sealed class MovieScreeningInfoChangedHandler(IUnitOfWork uow, MessageContext message, ILogger<MovieScreeningInfoChangedHandler> logger)
    : MovieCopyHandler<MovieScreeningInfoChangedEvent>(uow, message, logger)
{
    private readonly IUnitOfWork _uow = uow;

    protected override void Validate(MovieScreeningInfoChangedEvent e) =>
        ValidateSnapshot(e.MovieId, e.Title, e.NewDurationMinutes, e.NewAgeRating);

    protected override Task<bool> ApplyAsync(MovieScreeningInfoChangedEvent e, CancellationToken ct) =>
        _uow.Movies.UpsertAsync(new MovieSnapshot(e.MovieId, e.Title, e.NewDurationMinutes, e.NewAgeRating), e.OccurredAt, ct);

    protected override int MovieId(MovieScreeningInfoChangedEvent e) => e.MovieId;

    protected override string Describe(MovieScreeningInfoChangedEvent e) =>
        $"duration {e.OldDurationMinutes} → {e.NewDurationMinutes} хв, rating {e.OldAgeRating} → {e.NewAgeRating}";
}

/// <summary>Архівування (видалення в Каталозі) → IsArchived = 1: нові сеанси на фільм заборонені, історія лишається.</summary>
public sealed class MovieArchivedHandler(IUnitOfWork uow, MessageContext message, ILogger<MovieArchivedHandler> logger)
    : MovieCopyHandler<MovieArchivedEvent>(uow, message, logger)
{
    private readonly IUnitOfWork _uow = uow;

    protected override void Validate(MovieArchivedEvent e)
    {
        if (e.MovieId <= 0) throw new NonRetryableEventException($"Некоректний MovieId {e.MovieId}");
    }

    protected override Task<bool> ApplyAsync(MovieArchivedEvent e, CancellationToken ct) => _uow.Movies.ArchiveAsync(e.MovieId, e.OccurredAt, ct);

    protected override int MovieId(MovieArchivedEvent e) => e.MovieId;

    protected override string Describe(MovieArchivedEvent e) => $"archived «{e.Title}»";
}
