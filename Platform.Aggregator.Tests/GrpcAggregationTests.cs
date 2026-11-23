using System.Diagnostics;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Platform.Aggregator.Clients;
using Platform.Aggregator.Services;
using BookingGrpc = Booking.Grpc;
using CatalogGrpc = Catalog.Grpc;
using ReviewsGrpc = Reviews.Grpc;

namespace Platform.Aggregator.Tests;

/// <summary>Агрегатор на gRPC (Лаб. 06) з підмінним CallInvoker: мапінг повідомлень, NOT_FOUND, UNAVAILABLE, паралельність.</summary>
public class GrpcAggregationTests
{
    private static readonly DateTime StartsAt = new(2026, 11, 10, 18, 0, 0, DateTimeKind.Utc);

    private static object DefaultReply(string method) => method switch
    {
        "GetBooking" => new BookingGrpc.BookingReply
        {
            Id = 5, CustomerId = 1, SessionId = 3, Status = "Paid", MovieTitle = "Карпатський експрес",
            SessionStartsAt = Timestamp.FromDateTime(StartsAt), CreatedAt = Timestamp.FromDateTime(StartsAt.AddDays(-1)),
            TotalAmount = new BookingGrpc.DecimalValue { Units = 300, Nanos = 500_000_000 },
            Tickets = { new BookingGrpc.TicketReply { SeatId = 1, RowNumber = 2, SeatNumber = 7, SeatType = "Standard",
                Price = new BookingGrpc.DecimalValue { Units = 150, Nanos = 250_000_000 } } }
        },
        "GetSession" => new BookingGrpc.SessionReply
        {
            Id = 3, MovieId = 1, MovieTitle = "Карпатський експрес", HallName = "Зал 1",
            StartsAt = Timestamp.FromDateTime(StartsAt), BasePrice = new BookingGrpc.DecimalValue { Units = 150 }
        },
        "GetMovie" => new CatalogGrpc.MovieReply
        {
            Id = 1, Title = "Карпатський експрес", ReleaseYear = 2025, GenreName = "Пригоди", Description = "Потяг через гори",
            Credits = { new CatalogGrpc.CreditReply { PersonFullName = "Андрій Гнатюк", Role = "Director" } }
        },
        "GetMovieRating" => new ReviewsGrpc.MovieRatingReply { MovieId = 1, Average = 4.5, Count = 2, Distribution = { [4] = 1, [5] = 1 } },
        "GetLatestReviews" => new ReviewsGrpc.ReviewsReply
        {
            Reviews = { new ReviewsGrpc.ReviewReply { Id = "670000000000000000000001", MovieId = 1, AuthorName = "Олена К.",
                Kind = "full", Rating = 5, CreatedAt = Timestamp.FromDateTime(StartsAt) } }
        },
        _ => throw new InvalidOperationException(method)
    };

    private static AggregationService Create(FakeInvoker invoker) => new(
        new GrpcPlatformSources(
            new BookingGrpc.BookingService.BookingServiceClient(invoker),
            new CatalogGrpc.CatalogService.CatalogServiceClient(invoker),
            new ReviewsGrpc.ReviewsService.ReviewsServiceClient(invoker)),
        NullLogger<AggregationService>.Instance);

    [Fact]
    public async Task Protobuf_messages_are_mapped_to_card_including_money_and_optional_fields()
    {
        var card = await Create(new FakeInvoker(DefaultReply)).GetBookingCardAsync(5, TestContext.Current.CancellationToken);

        Assert.NotNull(card);
        Assert.Equal(300.5m, card.Booking.TotalAmount);              // DecimalValue units + nanos
        Assert.Equal("Потяг через гори", card.Movie!.Description);  // optional string задано
        Assert.Null(card.Movie.Country);                            // optional string не задано → null
        Assert.Equal("Андрій Гнатюк", card.Movie.Director);
        Assert.Equal(4.5, card.Rating!.Average);
        Assert.Equal(1, card.Rating.Distribution["5"]);
        Assert.Empty(card.Warnings);
        Assert.Equal(Transports.Grpc, new GrpcPlatformSources(null!, null!, null!).Transport);
    }

    [Fact]
    public async Task Grpc_NotFound_for_primary_resource_gives_null_card()
    {
        var invoker = new FakeInvoker(m => m == "GetBooking"
            ? throw new RpcException(new Status(StatusCode.NotFound, "Бронювання 999 не знайдено."))
            : DefaultReply(m));

        Assert.Null(await Create(invoker).GetBookingCardAsync(999, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(StatusCode.Unavailable)]
    [InlineData(StatusCode.DeadlineExceeded)]
    public async Task Grpc_failure_of_reviews_service_gives_partial_response(StatusCode status)
    {
        var invoker = new FakeInvoker(m => m is "GetMovieRating" or "GetLatestReviews"
            ? throw new RpcException(new Status(status, "reviews-api down"))
            : DefaultReply(m));

        var card = await Create(invoker).GetBookingCardAsync(5, TestContext.Current.CancellationToken);

        Assert.NotNull(card!.Movie);
        Assert.Null(card.Rating);
        Assert.StartsWith("reviews-api is unavailable", Assert.Single(card.Warnings));
    }

    [Fact]
    public async Task Independent_grpc_calls_run_in_parallel()
    {
        var invoker = new FakeInvoker(DefaultReply, delayFor: m => m is "GetMovie" or "GetMovieRating" or "GetLatestReviews" ? 300 : 0);

        var sw = Stopwatch.StartNew();
        await Create(invoker).GetBookingCardAsync(5, TestContext.Current.CancellationToken);

        Assert.True(sw.ElapsedMilliseconds < 800, $"3 gRPC-виклики по 300 мс мають іти паралельно, а тривали {sw.ElapsedMilliseconds} мс");
    }

    /// <summary>Підмінний CallInvoker: відповідь за назвою методу, необов'язкова затримка, винятки RpcException.</summary>
    private sealed class FakeInvoker(Func<string, object> respond, Func<string, int>? delayFor = null) : CallInvoker
    {
        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request)
        {
            async Task<TResponse> Respond()
            {
                var delay = delayFor?.Invoke(method.Name) ?? 0;
                if (delay > 0) await Task.Delay(delay, options.CancellationToken);
                return (TResponse)respond(method.Name);
            }

            return new AsyncUnaryCall<TResponse>(Respond(), Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess, () => [], () => { });
        }

        public override TResponse BlockingUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) =>
            throw new NotSupportedException();

        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options, TRequest request) =>
            throw new NotSupportedException();

        public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options) =>
            throw new NotSupportedException();

        public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string? host, CallOptions options) =>
            throw new NotSupportedException();
    }
}
