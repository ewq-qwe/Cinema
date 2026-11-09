using Platform.Aggregator.Models;

namespace Platform.Aggregator.Clients;

/// <summary>
/// Джерела даних агрегатора — однаковий набір операцій для двох транспортів:
/// <see cref="GrpcPlatformSources"/> (Лаб. 06, за замовчуванням) і <see cref="HttpPlatformSources"/> (REST з Лаб. 05,
/// лишено для вимірювання різниці затримок). 404 / NOT_FOUND → null; недоступність сервісу → виняток (його
/// перетворює на попередження CallTracker).
/// </summary>
public interface IPlatformSources
{
    string Transport { get; }

    Task<BookingResponse?> GetBookingAsync(long id, CancellationToken ct);
    Task<SessionResponse?> GetSessionAsync(long id, CancellationToken ct);
    Task<IReadOnlyList<SessionResponse>?> GetUpcomingSessionsAsync(int movieId, CancellationToken ct);
    Task<MovieResponse?> GetMovieAsync(int id, CancellationToken ct);
    Task<MovieRatingResponse?> GetMovieRatingAsync(int movieId, CancellationToken ct);
    Task<IReadOnlyList<ReviewResponse>?> GetLatestReviewsAsync(int movieId, int count, CancellationToken ct);
}

public static class Transports
{
    public const string Grpc = "grpc";
    public const string Http = "http";
}

/// <summary>REST-клієнти з Лаб. 05 за спільним інтерфейсом.</summary>
public sealed class HttpPlatformSources(BookingClient booking, CatalogClient catalog, ReviewsClient reviews) : IPlatformSources
{
    public string Transport => Transports.Http;

    public Task<BookingResponse?> GetBookingAsync(long id, CancellationToken ct) => booking.GetBookingAsync(id, ct);
    public Task<SessionResponse?> GetSessionAsync(long id, CancellationToken ct) => booking.GetSessionAsync(id, ct);
    public Task<IReadOnlyList<SessionResponse>?> GetUpcomingSessionsAsync(int movieId, CancellationToken ct) => booking.GetUpcomingSessionsAsync(movieId, ct);
    public Task<MovieResponse?> GetMovieAsync(int id, CancellationToken ct) => catalog.GetMovieAsync(id, ct);
    public Task<MovieRatingResponse?> GetMovieRatingAsync(int movieId, CancellationToken ct) => reviews.GetMovieRatingAsync(movieId, ct);
    public Task<IReadOnlyList<ReviewResponse>?> GetLatestReviewsAsync(int movieId, int count, CancellationToken ct) =>
        reviews.GetLatestReviewsAsync(movieId, count, ct);
}
