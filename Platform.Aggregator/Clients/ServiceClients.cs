using System.Net;
using Platform.Aggregator.Models;

namespace Platform.Aggregator.Clients;

// Typed HttpClients: по одному класу на сервіс. BaseAddress — логічне ім'я ресурсу AppHost ("https+http://booking-api"),
// реальну адресу підставляє service discovery. CorrelationIdHandler і resilience підключено для всіх клієнтів у ServiceDefaults.

public sealed class BookingClient(HttpClient http)
{
    public const string ServiceName = "booking-api";

    public Task<BookingResponse?> GetBookingAsync(long id, CancellationToken ct) =>
        http.GetOrNullAsync<BookingResponse>($"/api/bookings/{id}", ct);

    public Task<SessionResponse?> GetSessionAsync(long id, CancellationToken ct) =>
        http.GetOrNullAsync<SessionResponse>($"/api/sessions/{id}", ct);

    public async Task<IReadOnlyList<SessionResponse>?> GetUpcomingSessionsAsync(int movieId, CancellationToken ct) =>
        await http.GetOrNullAsync<List<SessionResponse>>($"/api/sessions?movieId={movieId}", ct);
}

public sealed class CatalogClient(HttpClient http)
{
    public const string ServiceName = "catalog-api";

    public Task<MovieResponse?> GetMovieAsync(int id, CancellationToken ct) =>
        http.GetOrNullAsync<MovieResponse>($"/api/movies/{id}", ct);
}

public sealed class ReviewsClient(HttpClient http)
{
    public const string ServiceName = "reviews-api";

    /// <summary>404 — на фільм ще немає відгуків (це не помилка).</summary>
    public Task<MovieRatingResponse?> GetMovieRatingAsync(int movieId, CancellationToken ct) =>
        http.GetOrNullAsync<MovieRatingResponse>($"/api/movies/{movieId}/rating", ct);

    public async Task<IReadOnlyList<ReviewResponse>?> GetLatestReviewsAsync(int movieId, int count, CancellationToken ct) =>
        (await http.GetOrNullAsync<PagedResponse<ReviewResponse>>($"/api/reviews?movieId={movieId}&sortBy=newest&pageSize={count}", ct))?.Items;
}

internal static class HttpClientExtensions
{
    /// <summary>GET + JSON. 404 → null (ресурсу немає); інші помилки → HttpRequestException (сервіс несправний).</summary>
    public static async Task<T?> GetOrNullAsync<T>(this HttpClient http, string uri, CancellationToken ct)
    {
        using var response = await http.GetAsync(uri, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return default;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(ct);
    }
}
