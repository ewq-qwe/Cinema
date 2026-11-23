using System.Diagnostics;
using Platform.Aggregator.Clients;
using Platform.Aggregator.Models;

namespace Platform.Aggregator.Services;

public interface IAggregationService
{
    Task<BookingCardDto?> GetBookingCardAsync(long bookingId, CancellationToken ct);
    Task<MovieCardDto?> GetMovieCardAsync(int movieId, CancellationToken ct);
}

/// <summary>
/// Aggregator Pattern: основний ресурс читається першим (без нього агрегувати нічого), а незалежні виклики
/// до різних сервісів виконуються ОДНОЧАСНО через Task.WhenAll. Транспорт — <see cref="IPlatformSources"/>:
/// gRPC (Лаб. 06, за замовчуванням) або HTTP (Лаб. 05). Відмова необов'язкового сервісу дає
/// часткову відповідь з попередженням у Warnings, а не 500.
/// </summary>
public sealed class AggregationService(
    IPlatformSources sources,
    ILogger<AggregationService> logger) : IAggregationService
{
    public const int LatestReviewsCount = 3;

    // Власні spans бізнес-операцій (джерело = ім'я застосунку, підключено в ServiceDefaults)
    private static readonly ActivitySource Source = new("Platform.Aggregator");

    public async Task<BookingCardDto?> GetBookingCardAsync(long bookingId, CancellationToken ct)
    {
        using var activity = Source.StartActivity("AggregateBookingCard");
        activity?.SetTag("booking.id", bookingId);
        activity?.SetTag("aggregator.transport", sources.Transport);
        var tracker = new CallTracker(logger, ct);

        // 1. Основні дані — бронювання. Без нього картки немає.
        var order = await tracker.RequiredAsync(BookingClient.ServiceName, $"booking {bookingId}",
            () => sources.GetBookingAsync(bookingId, ct));
        if (order is null) return null;

        // 2. Сеанс (той самий booking-api) — з нього відомо, який це фільм у Каталозі
        var session = await tracker.TryAsync(BookingClient.ServiceName, $"session {order.SessionId}",
            () => sources.GetSessionAsync(order.SessionId, ct));
        if (session.Status == CallStatus.NotFound)
            tracker.Warn($"Сеанс {order.SessionId} бронювання {bookingId} не знайдено.");
        if (session.Value is { } s && s.Id != order.SessionId)
            tracker.Warn($"Узгодженість: booking-api повернув сеанс {s.Id} замість {order.SessionId}.");

        MovieSummaryDto? movie = null;
        RatingSummaryDto? rating = null;
        IReadOnlyList<ReviewSummaryDto> latest = [];

        if (session.Value is { } sessionData)
        {
            var movieId = checked((int)sessionData.MovieId);
            activity?.SetTag("movie.id", movieId);

            // 3. Незалежні виклики catalog-api і reviews-api — паралельно
            var movieTask = tracker.TryAsync(CatalogClient.ServiceName, $"movie {movieId}", () => sources.GetMovieAsync(movieId, ct));
            var ratingTask = tracker.TryAsync(ReviewsClient.ServiceName, $"rating of movie {movieId}", () => sources.GetMovieRatingAsync(movieId, ct));
            var reviewsTask = tracker.TryAsync(ReviewsClient.ServiceName, $"latest reviews of movie {movieId}",
                () => sources.GetLatestReviewsAsync(movieId, LatestReviewsCount, ct));
            await Task.WhenAll(movieTask, ratingTask, reviewsTask);

            var movieResult = await movieTask;
            if (movieResult.Status == CallStatus.NotFound)
                tracker.Warn($"Фільм {movieId} з бронювання відсутній у Каталозі.");
            movie = movieResult.Value is { } m ? MapMovie(m) : null;

            // Узгодженість денормалізованих даних: назва фільму в Бронюванні — копія з Каталогу
            if (movie is not null && !string.Equals(movie.Title, sessionData.MovieTitle, StringComparison.Ordinal))
                tracker.Warn($"Назва фільму в Бронюванні («{sessionData.MovieTitle}») відрізняється від Каталогу («{movie.Title}»): копія довідника ще не оновлена.");

            rating = (await ratingTask).Value is { } r ? MapRating(r) : null;
            latest = FilterReviews((await reviewsTask).Value, movieId, tracker);
        }
        else if (session.Status != CallStatus.Unavailable)
        {
            tracker.Warn("Без сеансу неможливо визначити фільм — дані Каталогу й відгуків пропущено.");
        }

        return new BookingCardDto(
            MapBooking(order),
            session.Value is { } sv ? new SessionSummaryDto(sv.Id, sv.StartsAt, sv.HallName, sv.BasePrice, sv.MovieTitle) : null,
            movie, rating, latest, tracker.Warnings, tracker.Sources);
    }

    public async Task<MovieCardDto?> GetMovieCardAsync(int movieId, CancellationToken ct)
    {
        using var activity = Source.StartActivity("AggregateMovieCard");
        activity?.SetTag("movie.id", movieId);
        activity?.SetTag("aggregator.transport", sources.Transport);
        var tracker = new CallTracker(logger, ct);

        // 1. Основні дані — фільм з Каталогу
        var movie = await tracker.RequiredAsync(CatalogClient.ServiceName, $"movie {movieId}", () => sources.GetMovieAsync(movieId, ct));
        if (movie is null) return null;

        // 2. Сеанси (booking-api) і відгуки (reviews-api) — паралельно
        var sessionsTask = tracker.TryAsync(BookingClient.ServiceName, $"upcoming sessions of movie {movieId}",
            () => sources.GetUpcomingSessionsAsync(movieId, ct));
        var ratingTask = tracker.TryAsync(ReviewsClient.ServiceName, $"rating of movie {movieId}", () => sources.GetMovieRatingAsync(movieId, ct));
        var reviewsTask = tracker.TryAsync(ReviewsClient.ServiceName, $"latest reviews of movie {movieId}",
            () => sources.GetLatestReviewsAsync(movieId, LatestReviewsCount, ct));
        await Task.WhenAll(sessionsTask, ratingTask, reviewsTask);

        var sessions = ((await sessionsTask).Value ?? [])
            .Where(x =>
            {
                if (x.MovieId == movieId) return true;
                tracker.Warn($"Узгодженість: booking-api повернув сеанс {x.Id} іншого фільму — його відкинуто.");
                return false;
            })
            .OrderBy(x => x.StartsAt)
            .Select(x => new SessionSummaryDto(x.Id, x.StartsAt, x.HallName, x.BasePrice))
            .ToList();

        return new MovieCardDto(
            MapMovie(movie),
            sessions,
            (await ratingTask).Value is { } r ? MapRating(r) : null,
            FilterReviews((await reviewsTask).Value, movieId, tracker),
            tracker.Warnings,
            tracker.Sources);
    }

    private static IReadOnlyList<ReviewSummaryDto> FilterReviews(IReadOnlyList<ReviewResponse>? items, int movieId, CallTracker tracker)
    {
        if (items is null) return [];
        var own = items.Where(r => r.MovieId == movieId).ToList();
        if (own.Count != items.Count)
            tracker.Warn($"Узгодженість: reviews-api повернув відгуки інших фільмів — їх відкинуто ({items.Count - own.Count}).");
        return own.Select(r => new ReviewSummaryDto(r.Id, r.AuthorName, r.Kind, r.Rating, r.Title, r.Text, r.Likes, r.CreatedAt)).ToList();
    }

    private static BookingSummaryDto MapBooking(BookingResponse b) => new(
        b.Id, b.Status, b.CustomerId, b.TotalAmount, b.CreatedAt,
        b.Tickets.Where(t => !t.IsCancelled).Select(t => $"Ряд {t.RowNumber}, місце {t.SeatNumber} ({t.SeatType})").ToList(),
        b.Payment?.Method, b.Payment?.PaidAt, b.CancelReason);

    private static MovieSummaryDto MapMovie(MovieResponse m) => new(
        m.Id, m.Title, m.ReleaseYear, m.DurationMinutes, m.AgeRating, m.GenreName, m.Description, m.Country, m.PosterUrl,
        m.Credits.FirstOrDefault(c => c.Role == "Director")?.PersonFullName,
        m.Credits.Where(c => c.Role == "Actor").OrderBy(c => c.BillingOrder)
            .Select(c => c.CharacterName is null ? c.PersonFullName : $"{c.PersonFullName} ({c.CharacterName})").ToList());

    private static RatingSummaryDto MapRating(MovieRatingResponse r) => new(r.Average, r.Count, r.Distribution);
}
