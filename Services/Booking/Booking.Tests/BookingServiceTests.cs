using Booking.Bll.Dtos;
using Booking.Bll.Services;
using Booking.Dal.Abstractions;
using Booking.Domain;
using Booking.Domain.Exceptions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Booking.Tests;

public class BookingServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

    private readonly IUnitOfWork _uow = TestHelpers.CreateUow();
    private readonly BookingService _service;

    public BookingServiceTests() =>
        _service = new BookingService(_uow, TestHelpers.CreateMapper(), NullLogger<BookingService>.Instance, new FixedTimeProvider(Now));

    private static Session FutureSession() => new()
    {
        Id = 1, HallId = 1, StartsAt = Now.UtcDateTime.AddDays(1), BasePrice = 150m, HallName = "Зал 1",
        Movie = new Movie { Id = 1, Title = "Карпатський експрес" }
    };

    [Fact]
    public async Task Pay_nonexistent_booking_throws_NotFound_and_rolls_back()
    {
        _uow.Bookings.GetForUpdateAsync(42, Arg.Any<CancellationToken>()).Returns((Domain.Booking?)null);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _service.PayAsync(42, new PayBookingDto { Amount = 100, Method = "card", TransactionId = "T-1" }, TestContext.Current.CancellationToken));

        await _uow.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Pay_failure_in_second_repository_rolls_back_status_change()
    {
        _uow.Bookings.GetForUpdateAsync(5, Arg.Any<CancellationToken>()).Returns(new Domain.Booking
        {
            Id = 5, Status = BookingStatuses.Created, TotalAmount = 300m, SessionStartsAt = Now.UtcDateTime.AddDays(1)
        });
        _uow.Bookings.MarkPaidAsync(5, Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(true);
        // Другий репозиторій: TransactionId уже використано для іншого бронювання (UNIQUE у БД)
        _uow.Payments.AddAsync(Arg.Any<BookingPayment>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new BusinessConflictException("Запис з таким унікальним значенням уже існує."));

        await Assert.ThrowsAsync<BusinessConflictException>(() =>
            _service.PayAsync(5, new PayBookingDto { Amount = 300m, Method = "card", TransactionId = "USED" }, TestContext.Current.CancellationToken));

        await _uow.Bookings.Received(1).MarkPaidAsync(5, Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await _uow.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Pay_with_same_transaction_id_twice_is_idempotent()
    {
        _uow.Bookings.GetForUpdateAsync(5, Arg.Any<CancellationToken>()).Returns(new Domain.Booking
        {
            Id = 5, Status = BookingStatuses.Paid, TotalAmount = 300m, SessionStartsAt = Now.UtcDateTime.AddDays(1)
        });
        _uow.Payments.GetByTransactionIdAsync("T-1", Arg.Any<CancellationToken>())
            .Returns(new BookingPayment { BookingId = 5, TransactionId = "T-1", Amount = 300m });
        _uow.Bookings.GetWithDetailsAsync(5, Arg.Any<CancellationToken>()).Returns(new Domain.Booking { Id = 5, Status = BookingStatuses.Paid });

        var result = await _service.PayAsync(5, new PayBookingDto { Amount = 300m, Method = "card", TransactionId = "T-1" },
            TestContext.Current.CancellationToken);

        Assert.Equal(BookingStatuses.Paid, result.Status);
        await _uow.Bookings.DidNotReceive().MarkPaidAsync(Arg.Any<long>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await _uow.Payments.DidNotReceive().AddAsync(Arg.Any<BookingPayment>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_for_started_session_throws_Conflict_and_rolls_back()
    {
        var started = FutureSession();
        started.StartsAt = Now.UtcDateTime.AddMinutes(-5);
        _uow.Customers.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(new Customer { Id = 1 });
        _uow.Sessions.GetWithMovieAsync(1, Arg.Any<CancellationToken>()).Returns(started);

        await Assert.ThrowsAsync<BusinessConflictException>(() =>
            _service.CreateAsync(new CreateBookingDto { CustomerId = 1, SessionId = 1, SeatIds = [10] }, TestContext.Current.CancellationToken));

        await _uow.Bookings.DidNotReceive().AddAsync(Arg.Any<Domain.Booking>(), Arg.Any<CancellationToken>());
        await _uow.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_takes_title_and_prices_as_snapshot_and_commits()
    {
        _uow.Customers.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(new Customer { Id = 1 });
        _uow.Sessions.GetWithMovieAsync(1, Arg.Any<CancellationToken>()).Returns(FutureSession());
        _uow.Sessions.GetSeatsAsync(1, Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>()).Returns(
        [
            new Seat { Id = 10, HallId = 1, SeatType = SeatTypes.Standard },
            new Seat { Id = 33, HallId = 1, SeatType = SeatTypes.Vip }
        ]);
        _uow.Bookings.GetTakenSeatIdsAsync(1, Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>()).Returns([]);
        _uow.Bookings.AddAsync(Arg.Any<Domain.Booking>(), Arg.Any<CancellationToken>()).Returns(77L);
        _uow.Bookings.GetWithDetailsAsync(77, Arg.Any<CancellationToken>()).Returns(new Domain.Booking { Id = 77 });

        await _service.CreateAsync(new CreateBookingDto { CustomerId = 1, SessionId = 1, SeatIds = [10, 33] }, TestContext.Current.CancellationToken);

        await _uow.Bookings.Received(1).AddAsync(Arg.Is<Domain.Booking>(b =>
            b.MovieTitle == "Карпатський експрес" && b.TotalAmount == 150m + 225m), Arg.Any<CancellationToken>());
        await _uow.Bookings.Received(1).AddTicketsAsync(Arg.Is<IEnumerable<Ticket>>(t =>
            t.All(x => x.BookingId == 77) && t.Single(x => x.SeatId == 33).Price == 225m), Arg.Any<CancellationToken>());
        await _uow.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_with_taken_seat_throws_Conflict()
    {
        _uow.Customers.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(new Customer { Id = 1 });
        _uow.Sessions.GetWithMovieAsync(1, Arg.Any<CancellationToken>()).Returns(FutureSession());
        _uow.Sessions.GetSeatsAsync(1, Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>())
            .Returns([new Seat { Id = 10, HallId = 1 }]);
        _uow.Bookings.GetTakenSeatIdsAsync(1, Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>()).Returns([10]);

        await Assert.ThrowsAsync<BusinessConflictException>(() =>
            _service.CreateAsync(new CreateBookingDto { CustomerId = 1, SessionId = 1, SeatIds = [10] }, TestContext.Current.CancellationToken));
        await _uow.Received(1).RollbackAsync(Arg.Any<CancellationToken>());
    }
}
