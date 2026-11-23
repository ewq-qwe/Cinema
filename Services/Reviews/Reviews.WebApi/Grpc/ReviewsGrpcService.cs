using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using MediatR;
using Reviews.Application.Common.Interfaces;
using Reviews.Application.Common.Models;
using Reviews.Application.Movies.Queries.GetMovieRating;
using Reviews.Application.Reviews.Queries.GetReviewById;
using Reviews.Application.Reviews.Queries.GetReviewsList;
using Reviews.Domain.Exceptions;
using Reviews.Grpc;
using ValidationException = FluentValidation.ValidationException;

namespace Reviews.WebApi.Grpc;

/// <summary>
/// gRPC-адаптер, як і REST-контролер: лише ISender (MediatR) + перетворення DTO ↔ повідомлення.
/// ValidationException → INVALID_ARGUMENT, NotFoundException → NOT_FOUND, DomainException → FAILED_PRECONDITION.
/// </summary>
public sealed class ReviewsGrpcService(ISender mediator) : ReviewsService.ReviewsServiceBase
{
    public const int DefaultLatestCount = 3;

    public override Task<MovieRatingReply> GetMovieRating(GetMovieRatingRequest request, ServerCallContext context) =>
        Handle(async () =>
        {
            var rating = await mediator.Send(new GetMovieRatingQuery(request.MovieId), context.CancellationToken);
            var reply = new MovieRatingReply
            {
                MovieId = rating.MovieId,
                MovieTitle = rating.MovieTitle,
                Average = rating.Average,
                Count = rating.Count
            };
            reply.Distribution.Add(rating.Distribution.ToDictionary(kv => kv.Key, kv => kv.Value));
            return reply;
        });

    public override Task<ReviewsReply> GetLatestReviews(GetLatestReviewsRequest request, ServerCallContext context) =>
        Handle(async () =>
        {
            var count = request.Count == 0 ? DefaultLatestCount : request.Count;
            var page = await mediator.Send(
                new GetReviewsListQuery(MovieId: request.MovieId, SortBy: ReviewSort.Newest, PageSize: count),
                context.CancellationToken);
            var reply = new ReviewsReply { TotalCount = page.TotalCount };
            reply.Reviews.AddRange(page.Items.Select(ToReply));
            return reply;
        });

    public override Task<ReviewReply> GetReview(GetReviewRequest request, ServerCallContext context) =>
        Handle(async () => ToReply(await mediator.Send(new GetReviewByIdQuery(request.Id), context.CancellationToken)));

    private static async Task<T> Handle<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (ValidationException ex)
        {
            var detail = string.Join("; ", ex.Errors.Select(e => $"{e.PropertyName}: {e.ErrorMessage}"));
            throw new RpcException(new Status(StatusCode.InvalidArgument, detail));
        }
        catch (NotFoundException ex)
        {
            throw new RpcException(new Status(StatusCode.NotFound, ex.Message));
        }
        catch (DomainException ex)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, ex.Message));
        }
    }

    private static ReviewReply ToReply(ReviewDto r)
    {
        var reply = new ReviewReply
        {
            Id = r.Id,
            MovieId = r.MovieId,
            AuthorName = r.AuthorName,
            Kind = r.Kind,
            Rating = r.Rating,
            Likes = r.Likes,
            CommentsCount = r.CommentsCount,
            CreatedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(r.CreatedAt, DateTimeKind.Utc))
        };
        if (r.Title is not null) reply.Title = r.Title;
        if (r.Text is not null) reply.Text = r.Text;
        return reply;
    }
}
