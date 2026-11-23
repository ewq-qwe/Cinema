using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Platform.Aggregator.Models;
using BookingGrpc = Booking.Grpc;
using CatalogGrpc = Catalog.Grpc;
using ReviewsGrpc = Reviews.Grpc;

namespace Platform.Aggregator.Clients;

/// <summary>
/// Типізовані gRPC-клієнти (згенеровані з .proto сервісів, GrpcServices="Client") — HTTP/2 з мультиплексуванням.
/// Клієнти створює фабрика AddGrpcClient з адресою за логічним іменем, тож діють налаштування ServiceDefaults:
/// service discovery, resilience і CorrelationIdHandler (X-Correlation-Id потрапляє в метадані виклику).
/// Кожен виклик має CancellationToken і deadline; NOT_FOUND → null, інші статуси → RpcException.
/// </summary>
public sealed class GrpcPlatformSources(
    BookingGrpc.BookingService.BookingServiceClient booking,
    CatalogGrpc.CatalogService.CatalogServiceClient catalog,
    ReviewsGrpc.ReviewsService.ReviewsServiceClient reviews) : IPlatformSources
{
    /// <summary>Deadline одного виклику: повільний сервіс дає DEADLINE_EXCEEDED, а не тримає всю агрегацію.</summary>
    public static readonly TimeSpan CallDeadline = TimeSpan.FromSeconds(3);

    public string Transport => Transports.Grpc;

    public Task<BookingResponse?> GetBookingAsync(long id, CancellationToken ct) =>
        OrNull(async () => ToBooking(await booking.GetBookingAsync(
            new BookingGrpc.GetBookingRequest { Id = id }, deadline: Deadline(), cancellationToken: ct)));

    public Task<SessionResponse?> GetSessionAsync(long id, CancellationToken ct) =>
        OrNull(async () => ToSession(await booking.GetSessionAsync(
            new BookingGrpc.GetSessionRequest { Id = id }, deadline: Deadline(), cancellationToken: ct)));

    public Task<IReadOnlyList<SessionResponse>?> GetUpcomingSessionsAsync(int movieId, CancellationToken ct) =>
        OrNull<IReadOnlyList<SessionResponse>>(async () => (await booking.GetUpcomingSessionsAsync(
                new BookingGrpc.GetUpcomingSessionsRequest { MovieId = movieId }, deadline: Deadline(), cancellationToken: ct))
            .Sessions.Select(ToSession).ToList());

    public Task<MovieResponse?> GetMovieAsync(int id, CancellationToken ct) =>
        OrNull(async () => ToMovie(await catalog.GetMovieAsync(
            new CatalogGrpc.GetMovieRequest { Id = id }, deadline: Deadline(), cancellationToken: ct)));

    public Task<MovieRatingResponse?> GetMovieRatingAsync(int movieId, CancellationToken ct) =>
        OrNull(async () =>
        {
            var r = await reviews.GetMovieRatingAsync(
                new ReviewsGrpc.GetMovieRatingRequest { MovieId = movieId }, deadline: Deadline(), cancellationToken: ct);
            return new MovieRatingResponse(r.MovieId, r.MovieTitle, r.Average, r.Count,
                r.Distribution.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value));
        });

    public Task<IReadOnlyList<ReviewResponse>?> GetLatestReviewsAsync(int movieId, int count, CancellationToken ct) =>
        OrNull<IReadOnlyList<ReviewResponse>>(async () => (await reviews.GetLatestReviewsAsync(
                new ReviewsGrpc.GetLatestReviewsRequest { MovieId = movieId, Count = count }, deadline: Deadline(), cancellationToken: ct))
            .Reviews.Select(r => new ReviewResponse(r.Id, r.MovieId, r.AuthorName, r.Kind, r.Rating,
                r.HasTitle ? r.Title : null, r.HasText ? r.Text : null, null, r.Likes, r.CommentsCount, r.CreatedAt.ToDateTime()))
            .ToList());

    private static DateTime Deadline() => DateTime.UtcNow.Add(CallDeadline);

    /// <summary>NOT_FOUND — це «ресурсу немає» (як HTTP 404), а не відмова сервісу.</summary>
    private static async Task<T?> OrNull<T>(Func<Task<T>> call) where T : class
    {
        try
        {
            return await call();
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            return null;
        }
    }

    // ---------- Протобуф-повідомлення → контракти агрегатора ----------

    private static BookingResponse ToBooking(BookingGrpc.BookingReply b) => new(
        b.Id, b.CustomerId, b.SessionId, b.Status, b.MovieTitle, b.SessionStartsAt.ToDateTime(),
        ToDecimal(b.TotalAmount), b.HasCancelReason ? b.CancelReason : null, b.CreatedAt.ToDateTime(),
        b.Tickets.Select(t => new TicketResponse(t.SeatId, t.RowNumber, t.SeatNumber, t.SeatType, ToDecimal(t.Price), t.IsCancelled)).ToList(),
        b.Payment is { } p ? new PaymentResponse(ToDecimal(p.Amount), p.Method, p.PaidAt.ToDateTime()) : null);

    private static SessionResponse ToSession(BookingGrpc.SessionReply s) => new(
        s.Id, s.MovieId, s.MovieTitle, s.DurationMinutes, s.AgeRating, s.HallId, s.HallName,
        s.StartsAt.ToDateTime(), ToDecimal(s.BasePrice));

    private static MovieResponse ToMovie(CatalogGrpc.MovieReply m) => new(
        m.Id, m.Title, m.ReleaseYear, m.DurationMinutes, m.AgeRating, m.GenreName,
        m.HasDescription ? m.Description : null, m.HasCountry ? m.Country : null,
        m.HasPosterUrl ? m.PosterUrl : null, m.HasTrailerUrl ? m.TrailerUrl : null,
        m.Credits.Select(c => new CreditResponse(c.PersonFullName, c.Role, c.HasCharacterName ? c.CharacterName : null, c.BillingOrder)).ToList());

    private static decimal ToDecimal(BookingGrpc.DecimalValue? value) =>
        value is null ? 0m : value.Units + value.Nanos / 1_000_000_000m;
}
