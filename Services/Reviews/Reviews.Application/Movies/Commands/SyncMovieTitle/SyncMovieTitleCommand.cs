using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using Reviews.Application.Common.Caching;
using Reviews.Application.Common.Diagnostics;
using Reviews.Application.Common.Interfaces;
using Reviews.Application.Common.Messaging;

namespace Reviews.Application.Movies.Commands.SyncMovieTitle;

/// <summary>Результат синхронізації назви.</summary>
public enum SyncMovieTitleOutcome
{
    /// <summary>Назву оновлено у відгуках і обговореннях.</summary>
    Applied,

    /// <summary>Подію вже оброблено (повторна доставка) — нічого не змінено.</summary>
    Duplicate,

    /// <summary>Подія застаріла: вже застосовано новішу зміну назви (out-of-order).</summary>
    Stale
}

/// <summary>
/// Лаб. 08: Каталог перейменував фільм — оновити movieTitle в усіх відгуках і обговореннях (політика дублювання з Лаб. 01:
/// reviews.movieTitle / discussions.movieTitle — «копія, оновлюється подіями з Каталогу»). Команду надсилає споживач
/// подій у WebApi; Application нічого не знає про RabbitMQ.
/// </summary>
public sealed record SyncMovieTitleCommand(Guid EventId, int MovieId, string Title, DateTime OccurredAt) : ICommand<SyncMovieTitleOutcome>;

/// <remarks>
/// Ідемпотентність у MongoDB без транзакцій (контейнер Aspire — standalone mongod, а багатодокументні транзакції
/// потребують replica set): кожен крок сам ідемпотентний —
/// <list type="number">
/// <item>EventId уже в processedEvents → дублікат, вихід;</item>
/// <item>catalogMovies: умовний upsert за часом події → застаріла подія не перезапише новішу назву;</item>
/// <item>updateMany <c>$set movieTitle</c> — повторне виконання дає той самий стан;</item>
/// <item>EventId → processedEvents (унікальний _id).</item>
/// </list>
/// Якщо процес упаде між 3 і 4, повторна доставка повторить крок 3 з тим самим результатом — дані не зміняться вдруге.
/// </remarks>
public sealed class SyncMovieTitleCommandHandler(ICatalogSyncRepository sync, ITwoLevelCache cache, ILogger<SyncMovieTitleCommandHandler> logger)
    : IRequestHandler<SyncMovieTitleCommand, SyncMovieTitleOutcome>
{
    private const string EventType = "MovieRenamedEvent";

    public async Task<SyncMovieTitleOutcome> Handle(SyncMovieTitleCommand cmd, CancellationToken ct)
    {
        using var activity = ReviewsTelemetry.Source.StartActivity("movie.sync-title");
        activity?.SetTag("movie.id", cmd.MovieId);
        activity?.SetTag("event.id", cmd.EventId.ToString());

        if (await sync.IsProcessedAsync(cmd.EventId, ct))
        {
            logger.LogInformation("Duplicate event {EventType} {EventId} for movie {MovieId} skipped (already processed)", EventType, cmd.EventId, cmd.MovieId);
            return SyncMovieTitleOutcome.Duplicate;
        }

        if (!await sync.TryAdvanceTitleAsync(cmd.MovieId, cmd.Title, cmd.OccurredAt, ct))
        {
            await sync.MarkProcessedAsync(cmd.EventId, EventType, ct);
            logger.LogWarning("Movie {MovieId}: {EventType} {EventId} ({OccurredAt:O}) is stale — newer title already applied, skipped",
                cmd.MovieId, EventType, cmd.EventId, cmd.OccurredAt);
            return SyncMovieTitleOutcome.Stale;
        }

        var reviewIds = await sync.GetReviewIdsAsync(cmd.MovieId, ct);
        var (reviews, discussions) = await sync.RenameMovieAsync(cmd.MovieId, cmd.Title, ct);

        // Кеш (Лаб. 06): картки відгуків, рейтинг фільму (містить назву) і всі сторінки списків
        foreach (var id in reviewIds)
            await cache.RemoveAsync(ReviewsCacheKeys.Review(id), "MovieRenamed", ct);
        await cache.RemoveAsync(ReviewsCacheKeys.MovieRating(cmd.MovieId), "MovieRenamed", ct);
        await cache.BumpVersionAsync(ReviewsCacheKeys.ListVersion, "MovieRenamed", ct);

        await sync.MarkProcessedAsync(cmd.EventId, EventType, ct);
        activity?.SetTag("reviews.updated", reviews);
        logger.LogInformation("Movie {MovieId} renamed to «{MovieTitle}» by {EventType} {EventId}: {ReviewsUpdated} reviews, {DiscussionsUpdated} discussions updated",
            cmd.MovieId, cmd.Title, EventType, cmd.EventId, reviews, discussions);
        return SyncMovieTitleOutcome.Applied;
    }
}

public sealed class SyncMovieTitleCommandValidator : AbstractValidator<SyncMovieTitleCommand>
{
    public SyncMovieTitleCommandValidator()
    {
        RuleFor(x => x.EventId).NotEmpty();
        RuleFor(x => x.MovieId).GreaterThan(0);
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.OccurredAt).NotEmpty();
    }
}
