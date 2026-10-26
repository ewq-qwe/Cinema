using AutoMapper;
using Booking.Bll.Mapping;
using Booking.Dal.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Booking.Tests;

internal static class TestHelpers
{
    public static IMapper CreateMapper() =>
        new MapperConfiguration(cfg => cfg.AddMaps(typeof(BookingProfile).Assembly), NullLoggerFactory.Instance)
            .CreateMapper();

    /// <summary>Мок Unit of Work з окремими моками репозиторіїв.</summary>
    public static IUnitOfWork CreateUow()
    {
        var uow = Substitute.For<IUnitOfWork>();
        uow.Customers.Returns(Substitute.For<ICustomerRepository>());
        uow.Sessions.Returns(Substitute.For<ISessionRepository>());
        uow.Bookings.Returns(Substitute.For<IBookingRepository>());
        uow.Payments.Returns(Substitute.For<IPaymentRepository>());
        return uow;
    }
}

/// <summary>Фіксований «зараз» для детермінованих тестів.</summary>
internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
