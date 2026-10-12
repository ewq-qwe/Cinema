namespace Booking.Domain;

/// <summary>Глядач. Таблиця dbo.Customers.</summary>
public class Customer
{
    public long Id { get; set; }
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string? Phone { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public bool IsDeleted { get; set; }
    public byte[] RowVersion { get; set; } = [];           // оптимістична конкурентність
}

/// <summary>Сеанс. Таблиця dbo.Sessions (+ назва фільму й залу для читання).</summary>
public class Session
{
    public long Id { get; set; }
    public long MovieId { get; set; }
    public int HallId { get; set; }
    public DateTime StartsAt { get; set; }
    public decimal BasePrice { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    // Заповнюються лише запитами з JOIN (multi-mapping), у таблиці Sessions їх немає
    public Movie? Movie { get; set; }
    public string? HallName { get; set; }
}

/// <summary>Локальна копія фільму з Каталогу. Таблиця dbo.Movies.</summary>
public class Movie
{
    public long Id { get; set; }
    public string Title { get; set; } = "";
    public int DurationMinutes { get; set; }
    public string AgeRating { get; set; } = "";
}

/// <summary>Місце в залі. Таблиця dbo.Seats.</summary>
public class Seat
{
    public int Id { get; set; }
    public int HallId { get; set; }
    public byte RowNumber { get; set; }
    public byte SeatNumber { get; set; }
    public string SeatType { get; set; } = SeatTypes.Standard;
}

/// <summary>Місце на схемі залу для конкретного сеансу.</summary>
public class SeatAvailability
{
    public int SeatId { get; set; }
    public byte RowNumber { get; set; }
    public byte SeatNumber { get; set; }
    public string SeatType { get; set; } = SeatTypes.Standard;
    public bool IsFree { get; set; }
}

/// <summary>Бронювання — корінь агрегату. Таблиця dbo.Bookings.</summary>
public class Booking
{
    public long Id { get; set; }
    public long CustomerId { get; set; }
    public long SessionId { get; set; }
    public string Status { get; set; } = BookingStatuses.Created;
    public string MovieTitle { get; set; } = "";            // знімок на момент бронювання
    public DateTime SessionStartsAt { get; set; }           // знімок на момент бронювання
    public decimal TotalAmount { get; set; }
    public string? CancelReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public List<Ticket> Tickets { get; set; } = [];
    public BookingPayment? Payment { get; set; }
}

/// <summary>Квиток: позиція бронювання (M:N Bookings ↔ Seats). Таблиця dbo.Tickets.</summary>
public class Ticket
{
    public long BookingId { get; set; }
    public int SeatId { get; set; }
    public long SessionId { get; set; }
    public decimal Price { get; set; }                       // знімок ціни на момент бронювання
    public bool IsCancelled { get; set; }

    // Для відображення (JOIN із dbo.Seats)
    public byte RowNumber { get; set; }
    public byte SeatNumber { get; set; }
    public string SeatType { get; set; } = SeatTypes.Standard;
}

/// <summary>Оплата бронювання (1:1). Таблиця dbo.BookingPayments.</summary>
public class BookingPayment
{
    public long BookingId { get; set; }
    public decimal Amount { get; set; }
    public string Method { get; set; } = "";
    public string TransactionId { get; set; } = "";
    public DateTime PaidAt { get; set; }
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
