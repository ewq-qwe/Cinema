using Booking.Bll.Dtos;

namespace Booking.Bll.Services;

public interface ICustomerService
{
    Task<CustomerDto> GetAsync(long id, CancellationToken ct = default);
    Task<PagedResultDto<CustomerDto>> SearchAsync(string? term, int page, int pageSize, CancellationToken ct = default);
    Task<CustomerDto> CreateAsync(CreateCustomerDto input, CancellationToken ct = default);
    Task<CustomerDto> UpdateAsync(long id, UpdateCustomerDto input, CancellationToken ct = default);
    Task DeleteAsync(long id, CancellationToken ct = default);
}

public interface ISessionService
{
    Task<SessionDto> GetAsync(long id, CancellationToken ct = default);
    Task<IReadOnlyList<SessionDto>> GetUpcomingAsync(long? movieId, CancellationToken ct = default);
    Task<IReadOnlyList<SeatDto>> GetSeatMapAsync(long id, CancellationToken ct = default);
    Task<SessionDto> CreateAsync(SaveSessionDto input, CancellationToken ct = default);
    Task<SessionDto> UpdateAsync(long id, SaveSessionDto input, CancellationToken ct = default);
    Task DeleteAsync(long id, CancellationToken ct = default);
}

public interface IBookingService
{
    Task<BookingDto> GetAsync(long id, CancellationToken ct = default);
    Task<PagedResultDto<BookingDto>> GetByCustomerAsync(long customerId, int page, int pageSize, CancellationToken ct = default);
    /// <summary>Транзакція: знімок назви фільму й цін → Bookings + Tickets.</summary>
    Task<BookingDto> CreateAsync(CreateBookingDto input, CancellationToken ct = default);
    /// <summary>Транзакція через два репозиторії: Bookings (статус) + BookingPayments (оплата). Ідемпотентна за TransactionId.</summary>
    Task<BookingDto> PayAsync(long id, PayBookingDto input, CancellationToken ct = default);
    /// <summary>Скасування через процедуру usp_Booking_ChangeStatus (Created/Paid → Cancelled).</summary>
    Task<BookingDto> CancelAsync(long id, CancelBookingDto input, CancellationToken ct = default);
    /// <summary>Вхід у зал через процедуру usp_Booking_ChangeStatus (Paid → Used).</summary>
    Task<BookingDto> CheckInAsync(long id, CancellationToken ct = default);
}
