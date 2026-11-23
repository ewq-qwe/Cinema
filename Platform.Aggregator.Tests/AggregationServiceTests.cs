using System.Diagnostics;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Platform.Aggregator.Clients;
using Platform.Aggregator.Services;
using Platform.ServiceDefaults.Correlation;

namespace Platform.Aggregator.Tests;

/// <summary>Агрегатор з підмінними HTTP-відповідями сервісів: паралельність, часткові відмови, 404 / 503.</summary>
public class AggregationServiceTests
{
    private const string Booking = """
        {"id":5,"customerId":1,"sessionId":3,"status":"Paid","movieTitle":"Карпатський експрес","sessionStartsAt":"2026-11-10T18:00:00Z",
         "totalAmount":300,"createdAt":"2026-11-09T10:00:00Z","tickets":[{"seatId":1,"rowNumber":2,"seatNumber":7,"seatType":"Standard","price":150,"isCancelled":false}],
         "payment":{"amount":300,"method":"card","paidAt":"2026-11-09T10:05:00Z"}}
        """;
    private const string Session = """{"id":3,"movieId":1,"movieTitle":"Карпатський експрес","durationMinutes":118,"ageRating":"12+","hallId":1,"hallName":"Зал 1","startsAt":"2026-11-10T18:00:00Z","basePrice":150}""";
    private const string Movie = """
        {"id":1,"title":"Карпатський експрес","releaseYear":2025,"durationMinutes":118,"ageRating":"12+","genreName":"Пригоди",
         "credits":[{"personFullName":"Андрій Гнатюк","role":"Director","billingOrder":0},{"personFullName":"Олег Кравець","role":"Actor","characterName":"Мирон","billingOrder":1}]}
        """;
    private const string Rating = """{"movieId":1,"movieTitle":"Карпатський експрес","average":4.5,"count":2,"distribution":{"1":0,"2":0,"3":0,"4":1,"5":1}}""";
    private const string Reviews = """{"items":[{"id":"670000000000000000000001","movieId":1,"authorName":"Олена К.","kind":"full","rating":5,"likes":14,"commentsCount":2,"createdAt":"2026-10-02T18:30:00Z"}],"totalCount":1,"page":1,"pageSize":3}""";

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static AggregationService Create(
        Func<string, Task<HttpResponseMessage>>? booking = null,
        Func<string, Task<HttpResponseMessage>>? catalog = null,
        Func<string, Task<HttpResponseMessage>>? reviews = null)
    {
        booking ??= path => Task.FromResult(path.StartsWith("/api/sessions") ? Json(Session) : Json(Booking));
        catalog ??= _ => Task.FromResult(Json(Movie));
        reviews ??= path => Task.FromResult(path.Contains("/rating") ? Json(Rating) : Json(Reviews));

        static HttpClient Client(string host, Func<string, Task<HttpResponseMessage>> respond) =>
            new(new StubHandler(respond)) { BaseAddress = new Uri($"http://{host}") };

        return new AggregationService(
            new HttpPlatformSources(
                new BookingClient(Client("booking-api", booking)),
                new CatalogClient(Client("catalog-api", catalog)),
                new ReviewsClient(Client("reviews-api", reviews))),
            NullLogger<AggregationService>.Instance);
    }

    private static Task<HttpResponseMessage> Down(string _) =>
        throw new HttpRequestException("Connection refused (reviews-api:80)");

    [Fact]
    public async Task Full_booking_card_combines_three_services_without_warnings()
    {
        var card = await Create().GetBookingCardAsync(5, TestContext.Current.CancellationToken);

        Assert.NotNull(card);
        Assert.Equal("Paid", card.Booking.Status);
        Assert.Equal(["Ряд 2, місце 7 (Standard)"], card.Booking.Seats);
        Assert.Equal("Андрій Гнатюк", card.Movie!.Director);
        Assert.Equal(4.5, card.Rating!.Average);
        Assert.Single(card.LatestReviews);
        Assert.Empty(card.Warnings);
        Assert.Equal(5, card.Sources.Count);   // booking, session, movie, rating, reviews
    }

    [Fact]
    public async Task Unavailable_reviews_api_gives_partial_response_with_warning()
    {
        var card = await Create(reviews: Down).GetBookingCardAsync(5, TestContext.Current.CancellationToken);

        Assert.NotNull(card);
        Assert.NotNull(card.Movie);              // каталог працює
        Assert.Null(card.Rating);
        Assert.Empty(card.LatestReviews);
        var warning = Assert.Single(card.Warnings);   // два виклики reviews-api → одне попередження
        Assert.StartsWith("reviews-api is unavailable", warning);
        Assert.Equal(2, card.Sources.Count(s => s.Status == nameof(CallStatus.Unavailable)));
    }

    [Fact]
    public async Task Catalog_and_reviews_are_called_in_parallel()
    {
        var delayed = new Func<string, Task<HttpResponseMessage>>(async path =>
        {
            await Task.Delay(300);
            return path.Contains("/rating") ? Json(Rating) : path.StartsWith("/api/reviews") ? Json(Reviews) : Json(Movie);
        });

        var sw = Stopwatch.StartNew();
        var card = await Create(catalog: delayed, reviews: delayed).GetBookingCardAsync(5, TestContext.Current.CancellationToken);
        sw.Stop();

        Assert.NotNull(card);
        Assert.True(sw.ElapsedMilliseconds < 800, $"3 виклики по 300 мс мають іти паралельно, а тривали {sw.ElapsedMilliseconds} мс");
    }

    [Fact]
    public async Task Missing_booking_returns_null_for_404()
    {
        var service = Create(booking: _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));
        Assert.Null(await service.GetBookingCardAsync(999, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Unavailable_primary_service_throws_for_503()
    {
        var service = Create(catalog: Down);
        await Assert.ThrowsAsync<UpstreamUnavailableException>(() => service.GetMovieCardAsync(1, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Title_mismatch_between_booking_copy_and_catalog_is_reported()
    {
        var renamed = Movie.Replace("\"title\":\"Карпатський експрес\"", "\"title\":\"Карпатський експрес (режисерська версія)\"");
        var card = await Create(catalog: _ => Task.FromResult(Json(renamed))).GetBookingCardAsync(5, TestContext.Current.CancellationToken);

        Assert.Contains(card!.Warnings, w => w.Contains("відрізняється від Каталогу"));
    }

    [Fact]
    public async Task Movie_card_keeps_only_sessions_of_this_movie()
    {
        var sessions = $"[{Session},{Session.Replace("\"id\":3", "\"id\":9").Replace("\"movieId\":1", "\"movieId\":2")}]";
        var card = await Create(booking: _ => Task.FromResult(Json(sessions))).GetMovieCardAsync(1, TestContext.Current.CancellationToken);

        Assert.Single(card!.UpcomingSessions);
        Assert.Contains(card.Warnings, w => w.Contains("іншого фільму"));
    }

    [Fact]
    public async Task CorrelationIdHandler_copies_incoming_header_to_outgoing_request()
    {
        var http = new DefaultHttpContext();
        http.Request.Headers[CorrelationIdMiddleware.HeaderName] = "corr-42";
        string? seen = null;
        var handler = new CorrelationIdHandler(new HttpContextAccessor { HttpContext = http })
        {
            InnerHandler = new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)), r =>
                seen = r.Headers.TryGetValues(CorrelationIdMiddleware.HeaderName, out var v) ? v.Single() : null)
        };

        using var client = new HttpClient(handler);
        await client.GetAsync("http://catalog-api/api/movies/1", TestContext.Current.CancellationToken);

        Assert.Equal("corr-42", seen);
    }

    private sealed class StubHandler(Func<string, Task<HttpResponseMessage>> respond, Action<HttpRequestMessage>? inspect = null)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            inspect?.Invoke(request);
            return respond(request.RequestUri!.PathAndQuery);
        }
    }
}
