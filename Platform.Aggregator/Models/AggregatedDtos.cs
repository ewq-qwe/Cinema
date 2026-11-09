namespace Platform.Aggregator.Models;

// Агреговані DTO відображають бізнес-сценарій («картка бронювання», «картка фільму»),
// а не копіюють структури окремих сервісів. null-поля не серіалізуються (JsonIgnoreCondition.WhenWritingNull).

/// <summary>Картка бронювання: квитки з booking-api + фільм з catalog-api + рейтинг і відгуки з reviews-api.</summary>
public sealed record BookingCardDto(
    BookingSummaryDto Booking,
    SessionSummaryDto? Session,
    MovieSummaryDto? Movie,
    RatingSummaryDto? Rating,
    IReadOnlyList<ReviewSummaryDto> LatestReviews,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<SourceCallDto> Sources);

/// <summary>Картка фільму: фільм з catalog-api + найближчі сеанси з booking-api + рейтинг і відгуки з reviews-api.</summary>
public sealed record MovieCardDto(
    MovieSummaryDto Movie,
    IReadOnlyList<SessionSummaryDto> UpcomingSessions,
    RatingSummaryDto? Rating,
    IReadOnlyList<ReviewSummaryDto> LatestReviews,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<SourceCallDto> Sources);

public sealed record BookingSummaryDto(
    long Id, string Status, long CustomerId, decimal TotalAmount, DateTime CreatedAt,
    IReadOnlyList<string> Seats, string? PaymentMethod, DateTime? PaidAt, string? CancelReason);

public sealed record SessionSummaryDto(long Id, DateTime StartsAt, string HallName, decimal BasePrice, string? MovieTitle = null);

public sealed record MovieSummaryDto(
    int Id, string Title, int ReleaseYear, int DurationMinutes, string AgeRating, string Genre,
    string? Description, string? Country, string? PosterUrl, string? Director, IReadOnlyList<string> Cast);

public sealed record RatingSummaryDto(double Average, int Count, IReadOnlyDictionary<string, int> Distribution);

public sealed record ReviewSummaryDto(string Id, string Author, string Kind, int Rating, string? Title, string? Text, int Likes, DateTime CreatedAt);

/// <summary>Звіт про кожен виклик сервісу: видно, що виклики йшли паралельно й скільки тривали.</summary>
public sealed record SourceCallDto(string Service, string Operation, string Status, long ElapsedMs);
