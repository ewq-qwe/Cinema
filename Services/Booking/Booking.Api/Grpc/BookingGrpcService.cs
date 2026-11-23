using Booking.Bll.Dtos;
using Booking.Bll.Services;
using Booking.Domain.Exceptions;
using Booking.Grpc;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using BookingServiceBase = Booking.Grpc.BookingService.BookingServiceBase;

namespace Booking.Api.Grpc;

/// <summary>
/// gRPC-адаптер над BLL з Лаб. 02 (як і контролери): лише перетворення DTO ↔ повідомлення та винятків ↔ статусів.
/// NotFound → NOT_FOUND, Validation → INVALID_ARGUMENT, конфлікт → FAILED_PRECONDITION.
/// </summary>
public sealed class BookingGrpcService(IBookingService bookings, ISessionService sessions) : BookingServiceBase
{
    public override Task<BookingReply> GetBooking(GetBookingRequest request, ServerCallContext context) =>
        Handle(request.Id, "бронювання", async () => ToReply(await bookings.GetAsync(request.Id, context.CancellationToken)));

    public override Task<SessionReply> GetSession(GetSessionRequest request, ServerCallContext context) =>
        Handle(request.Id, "сеансу", async () => ToReply(await sessions.GetAsync(request.Id, context.CancellationToken)));

    public override Task<SessionsReply> GetUpcomingSessions(GetUpcomingSessionsRequest request, ServerCallContext context) =>
        Handle(request.MovieId, "фільму", async () =>
        {
            var reply = new SessionsReply();
            reply.Sessions.AddRange((await sessions.GetUpcomingAsync(request.MovieId, context.CancellationToken)).Select(ToReply));
            return reply;
        });

    private static async Task<T> Handle<T>(long id, string what, Func<Task<T>> action)
    {
        if (id <= 0)
            throw new RpcException(new Status(StatusCode.InvalidArgument, $"Id {what} має бути додатним."));
        try
        {
            return await action();
        }
        catch (NotFoundException ex)
        {
            throw new RpcException(new Status(StatusCode.NotFound, ex.Message));
        }
        catch (ValidationException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }
        catch (BusinessConflictException ex)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, ex.Message));
        }
    }

    private static BookingReply ToReply(BookingDto b)
    {
        var reply = new BookingReply
        {
            Id = b.Id,
            CustomerId = b.CustomerId,
            SessionId = b.SessionId,
            Status = b.Status,
            MovieTitle = b.MovieTitle,
            SessionStartsAt = ToTimestamp(b.SessionStartsAt),
            TotalAmount = ToDecimal(b.TotalAmount),
            CreatedAt = ToTimestamp(b.CreatedAt)
        };
        if (b.CancelReason is not null) reply.CancelReason = b.CancelReason;
        if (b.Payment is { } p)
            reply.Payment = new PaymentReply { Amount = ToDecimal(p.Amount), Method = p.Method, PaidAt = ToTimestamp(p.PaidAt) };
        reply.Tickets.AddRange(b.Tickets.Select(t => new TicketReply
        {
            SeatId = t.SeatId,
            RowNumber = t.RowNumber,
            SeatNumber = t.SeatNumber,
            SeatType = t.SeatType,
            Price = ToDecimal(t.Price),
            IsCancelled = t.IsCancelled
        }));
        return reply;
    }

    private static SessionReply ToReply(SessionDto s) => new()
    {
        Id = s.Id,
        MovieId = s.MovieId,
        MovieTitle = s.MovieTitle,
        DurationMinutes = s.DurationMinutes,
        AgeRating = s.AgeRating,
        HallId = s.HallId,
        HallName = s.HallName,
        StartsAt = ToTimestamp(s.StartsAt),
        BasePrice = ToDecimal(s.BasePrice)
    };

    // У БД час зберігається в UTC (datetime2 без зони) — позначаємо Kind явно, як вимагає Timestamp
    private static Timestamp ToTimestamp(DateTime value) =>
        Timestamp.FromDateTime(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static DecimalValue ToDecimal(decimal value)
    {
        var units = decimal.Truncate(value);
        return new DecimalValue { Units = (long)units, Nanos = (int)((value - units) * 1_000_000_000m) };
    }
}
