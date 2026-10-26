using System.Data;
using AutoMapper;
using Booking.Bll.Dtos;
using Booking.Dal.Abstractions;
using Booking.Domain;
using Booking.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace Booking.Bll.Services;

/// <summary>
/// Бізнес-логіка бронювань. Транзакційні операції — через Unit of Work:
/// <list type="bullet">
/// <item><see cref="CreateAsync"/> — Sessions (читання + знімок) → Bookings + Tickets;</item>
/// <item><see cref="PayAsync"/> — Bookings (статус) + BookingPayments (оплата), два репозиторії.</item>
/// </list>
/// Рівень ізоляції — ReadCommitted: найменше блокувань; там, де потрібна сильніша гарантія,
/// застосовано точкові блокування (UPDLOCK/HOLDLOCK у репозиторіях) і UNIQUE-індекси БД.
/// </summary>
public sealed class BookingService(IUnitOfWork uow, IMapper mapper, ILogger<BookingService> logger, TimeProvider clock)
    : IBookingService
{
    private const string UserName = "booking-api";

    public async Task<BookingDto> GetAsync(long id, CancellationToken ct = default)
    {
        var booking = await uow.Bookings.GetWithDetailsAsync(id, ct)
                      ?? throw new NotFoundException($"Бронювання {id} не знайдено.");
        return mapper.Map<BookingDto>(booking);
    }

    public async Task<PagedResultDto<BookingDto>> GetByCustomerAsync(long customerId, int page, int pageSize, CancellationToken ct = default)
    {
        _ = await uow.Customers.GetByIdAsync(customerId, ct) ?? throw new NotFoundException($"Глядача {customerId} не знайдено.");
        (page, pageSize) = Paging.Normalize(page, pageSize);
        return Paging.Map<Domain.Booking, BookingDto>(await uow.Bookings.GetByCustomerAsync(customerId, page, pageSize, ct), mapper);
    }

    public async Task<BookingDto> CreateAsync(CreateBookingDto input, CancellationToken ct = default)
    {
        var seatIds = input.SeatIds.Distinct().ToList();
        if (seatIds.Count != input.SeatIds.Count)
            throw new ValidationException("Місця в бронюванні не повинні повторюватися.",
                new Dictionary<string, string[]> { ["seatIds"] = ["Є дублікати."] });

        long bookingId;
        await uow.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            _ = await uow.Customers.GetByIdAsync(input.CustomerId, ct)
                ?? throw new NotFoundException($"Глядача {input.CustomerId} не знайдено.");

            var session = await uow.Sessions.GetWithMovieAsync(input.SessionId, ct)
                          ?? throw new NotFoundException($"Сеанс {input.SessionId} не знайдено.");
            if (session.StartsAt <= clock.GetUtcNow().UtcDateTime)
                throw new BusinessConflictException("Сеанс уже почався — бронювання неможливе.");

            var seats = await uow.Sessions.GetSeatsAsync(session.HallId, seatIds, ct);
            if (seats.Count != seatIds.Count)
                throw new ValidationException("Одне або кілька місць не належать залу цього сеансу.",
                    new Dictionary<string, string[]> { ["seatIds"] = [$"Допустимі лише місця залу {session.HallName}."] });

            var taken = await uow.Bookings.GetTakenSeatIdsAsync(session.Id, seatIds, ct);
            if (taken.Count > 0)
                throw new BusinessConflictException($"Місця вже зайняті: {string.Join(", ", taken)}.");

            // Знімок на момент бронювання (політика дублювання з Лаб. 01): назва фільму, час і ціни
            // фіксуються в бронюванні та квитках і не змінюються, навіть якщо змінять фільм чи ціну сеансу.
            var tickets = seats.Select(s => new Ticket
            {
                SeatId = s.Id,
                SessionId = session.Id,
                Price = s.SeatType == SeatTypes.Vip
                    ? Math.Round(session.BasePrice * SeatTypes.VipMultiplier, 2)
                    : session.BasePrice
            }).ToList();

            bookingId = await uow.Bookings.AddAsync(new Domain.Booking
            {
                CustomerId = input.CustomerId,
                SessionId = session.Id,
                Status = BookingStatuses.Created,
                MovieTitle = session.Movie!.Title,
                SessionStartsAt = session.StartsAt,
                TotalAmount = tickets.Sum(t => t.Price),
                CreatedBy = UserName
            }, ct);

            tickets.ForEach(t => t.BookingId = bookingId);
            // Якщо паралельний запит устиг продати місце, спрацює UNIQUE-індекс → 409 і відкат бронювання вище
            await uow.Bookings.AddTicketsAsync(tickets, ct);

            await uow.CommitAsync(ct);
        }
        catch
        {
            await uow.RollbackAsync(ct);
            throw;
        }

        logger.LogInformation("Створено бронювання {BookingId} на сеанс {SessionId}, місць: {Seats}",
            bookingId, input.SessionId, seatIds.Count);
        return await GetAsync(bookingId, ct);
    }

    public async Task<BookingDto> PayAsync(long id, PayBookingDto input, CancellationToken ct = default)
    {
        await uow.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            // Песимістичне блокування рядка: дві паралельні оплати одного бронювання не пройдуть обидві
            var booking = await uow.Bookings.GetForUpdateAsync(id, ct)
                          ?? throw new NotFoundException($"Бронювання {id} не знайдено.");

            // Ідемпотентність: повторний запит з тим самим TransactionId не оплачує вдруге
            var existing = await uow.Payments.GetByTransactionIdAsync(input.TransactionId, ct);
            if (existing?.BookingId == id)
            {
                await uow.CommitAsync(ct);
                logger.LogInformation("Повторна оплата бронювання {BookingId} з тим самим TransactionId — пропущено", id);
                return await GetAsync(id, ct);
            }

            if (booking.Status != BookingStatuses.Created)
                throw new BusinessConflictException($"Оплатити можна лише нове бронювання (поточний статус: {booking.Status}).");
            if (booking.SessionStartsAt <= clock.GetUtcNow().UtcDateTime)
                throw new BusinessConflictException("Сеанс уже почався — оплата неможлива.");
            if (input.Amount != booking.TotalAmount)
                throw new ValidationException("Сума оплати не збігається з вартістю бронювання.",
                    new Dictionary<string, string[]> { ["amount"] = [$"Очікується {booking.TotalAmount:0.00}."] });

            // Репозиторій 1: статус Created → Paid
            await uow.Bookings.MarkPaidAsync(id, UserName, ct);

            // Репозиторій 2: запис оплати. Якщо TransactionId уже використано для ІНШОГО бронювання,
            // UNIQUE-індекс БД відхилить вставку → BusinessConflictException → відкат зміни статусу вище.
            await uow.Payments.AddAsync(new BookingPayment
            {
                BookingId = id,
                Amount = input.Amount,
                Method = input.Method,
                TransactionId = input.TransactionId
            }, ct);

            await uow.CommitAsync(ct);
        }
        catch
        {
            await uow.RollbackAsync(ct);
            throw;
        }

        logger.LogInformation("Бронювання {BookingId} оплачено", id);
        return await GetAsync(id, ct);
    }

    public async Task<BookingDto> CancelAsync(long id, CancelBookingDto input, CancellationToken ct = default)
    {
        // Перевірку переходу й звільнення місць виконує процедура з Лаб. 01 в явній транзакції;
        // її керовані помилки (50001/50002/50003/50006) DAL перетворює на доменні винятки
        await uow.Bookings.ChangeStatusAsync(id, BookingStatuses.Cancelled, input.Reason,
            RowVersions.DecodeOptional(input.RowVersion), UserName, ct);
        logger.LogInformation("Бронювання {BookingId} скасовано", id);
        return await GetAsync(id, ct);
    }

    public async Task<BookingDto> CheckInAsync(long id, CancellationToken ct = default)
    {
        await uow.Bookings.ChangeStatusAsync(id, BookingStatuses.Used, null, null, UserName, ct);
        logger.LogInformation("Бронювання {BookingId}: глядач увійшов до зали", id);
        return await GetAsync(id, ct);
    }
}
