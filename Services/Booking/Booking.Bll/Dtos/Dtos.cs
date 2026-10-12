using System.ComponentModel.DataAnnotations;

namespace Booking.Bll.Dtos;

// =====================================================================
// DTO — лише дані для API-контрактів, без логіки.
// Вхідні DTO мають DataAnnotations: некоректний запит відхиляється ([ApiController] → 400)
// ще до виклику сервісу. Правила, що потребують стану БД, перевіряє сервіс (BLL).
// =====================================================================

public sealed class PagedResultDto<T>
{
    public IReadOnlyList<T> Items { get; init; } = [];
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
}

// ---------- Глядачі ----------

public sealed class CustomerDto
{
    public long Id { get; init; }
    public string FullName { get; init; } = "";
    public string Email { get; init; } = "";
    public string? Phone { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
    /// <summary>Версія рядка (base64). Передайте її в PUT — так виявляється конфлікт паралельних змін.</summary>
    public string RowVersion { get; init; } = "";
}

public class CreateCustomerDto
{
    [Required, StringLength(150, MinimumLength = 2)]
    public string FullName { get; set; } = "";

    [Required, EmailAddress, StringLength(254)]
    public string Email { get; set; } = "";

    [RegularExpression(@"^\+380\d{9}$", ErrorMessage = "Телефон у форматі +380XXXXXXXXX.")]
    public string? Phone { get; set; }
}

public sealed class UpdateCustomerDto : CreateCustomerDto
{
    [Required]
    public string RowVersion { get; set; } = "";
}

// ---------- Сеанси ----------

public sealed class SessionDto
{
    public long Id { get; init; }
    public long MovieId { get; init; }
    public string MovieTitle { get; init; } = "";
    public int DurationMinutes { get; init; }
    public string AgeRating { get; init; } = "";
    public int HallId { get; init; }
    public string HallName { get; init; } = "";
    public DateTime StartsAt { get; init; }
    public decimal BasePrice { get; init; }
}

public sealed class SaveSessionDto
{
    [Range(1, long.MaxValue)]
    public long MovieId { get; set; }

    [Range(1, int.MaxValue)]
    public int HallId { get; set; }

    /// <summary>Час початку (UTC), має бути в майбутньому.</summary>
    [Required]
    public DateTime? StartsAt { get; set; }

    // Межі задано рядком, тому розбираємо їх в інваріантній культурі (в uk-UA роздільник — кома)
    [Range(typeof(decimal), "1", "10000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    public decimal BasePrice { get; set; }
}

public sealed class SeatDto
{
    public int SeatId { get; init; }
    public byte RowNumber { get; init; }
    public byte SeatNumber { get; init; }
    public string SeatType { get; init; } = "";
    public bool IsFree { get; init; }
}

// ---------- Бронювання ----------

public sealed class TicketDto
{
    public int SeatId { get; init; }
    public byte RowNumber { get; init; }
    public byte SeatNumber { get; init; }
    public string SeatType { get; init; } = "";
    public decimal Price { get; init; }
    public bool IsCancelled { get; init; }
}

public sealed class PaymentDto
{
    public decimal Amount { get; init; }
    public string Method { get; init; } = "";
    public string TransactionId { get; init; } = "";
    public DateTime PaidAt { get; init; }
}

public sealed class BookingDto
{
    public long Id { get; init; }
    public long CustomerId { get; init; }
    public long SessionId { get; init; }
    public string Status { get; init; } = "";
    public string MovieTitle { get; init; } = "";
    public DateTime SessionStartsAt { get; init; }
    public decimal TotalAmount { get; init; }
    public string? CancelReason { get; init; }
    public DateTime CreatedAt { get; init; }
    public string RowVersion { get; init; } = "";
    public IReadOnlyList<TicketDto> Tickets { get; init; } = [];
    public PaymentDto? Payment { get; init; }
}

public sealed class CreateBookingDto
{
    [Range(1, long.MaxValue)]
    public long CustomerId { get; set; }

    [Range(1, long.MaxValue)]
    public long SessionId { get; set; }

    [Required, MinLength(1), MaxLength(10)]
    public List<int> SeatIds { get; set; } = [];
}

public sealed class PayBookingDto
{
    // "0.01" розбирається в інваріантній культурі — інакше на uk-UA Windows атрибут кидає ArgumentException (500)
    [Range(typeof(decimal), "0.01", "1000000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    public decimal Amount { get; set; }

    [Required, RegularExpression("^(card|cash|online)$", ErrorMessage = "Спосіб оплати: card, cash або online.")]
    public string Method { get; set; } = "";

    /// <summary>Ідентифікатор транзакції платіжної системи — він же ключ ідемпотентності.</summary>
    [Required, StringLength(64, MinimumLength = 3)]
    public string TransactionId { get; set; } = "";
}

public sealed class CancelBookingDto
{
    [StringLength(300)]
    public string? Reason { get; set; }

    /// <summary>Необов'язково: версія (base64), яку бачив користувач — для виявлення конфлікту.</summary>
    public string? RowVersion { get; set; }
}
