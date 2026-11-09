namespace Platform.Aggregator.Models;

// Контракти відповідей мікросервісів — лише поля, потрібні агрегатору (System.Text.Json, camelCase).
// Aggregator не посилається на проєкти сервісів: залежність — тільки від їхнього HTTP API.

// ---------- booking-api (Лаб. 02) ----------
public sealed record BookingResponse(
    long Id, long CustomerId, long SessionId, string Status, string MovieTitle, DateTime SessionStartsAt,
    decimal TotalAmount, string? CancelReason, DateTime CreatedAt,
    IReadOnlyList<TicketResponse> Tickets, PaymentResponse? Payment);

public sealed record TicketResponse(int SeatId, int RowNumber, int SeatNumber, string SeatType, decimal Price, bool IsCancelled);

public sealed record PaymentResponse(decimal Amount, string Method, DateTime PaidAt);

public sealed record SessionResponse(
    long Id, long MovieId, string MovieTitle, int DurationMinutes, string AgeRating,
    int HallId, string HallName, DateTime StartsAt, decimal BasePrice);

// ---------- catalog-api (Лаб. 03) ----------
public sealed record MovieResponse(
    int Id, string Title, int ReleaseYear, int DurationMinutes, string AgeRating, string GenreName,
    string? Description, string? Country, string? PosterUrl, string? TrailerUrl,
    IReadOnlyList<CreditResponse> Credits);

public sealed record CreditResponse(string PersonFullName, string Role, string? CharacterName, int BillingOrder);

// ---------- reviews-api (Лаб. 04) ----------
public sealed record MovieRatingResponse(int MovieId, string MovieTitle, double Average, int Count, IReadOnlyDictionary<string, int> Distribution);

public sealed record ReviewResponse(
    string Id, int MovieId, string AuthorName, string Kind, int Rating, string? Title, string? Text,
    bool? HasSpoilers, int Likes, int CommentsCount, DateTime CreatedAt);

public sealed record PagedResponse<T>(IReadOnlyList<T> Items, long TotalCount, int Page, int PageSize);
