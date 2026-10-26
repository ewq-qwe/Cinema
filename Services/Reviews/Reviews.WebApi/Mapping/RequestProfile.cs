using AutoMapper;
using Reviews.Application.Discussions.Commands.PostMessage;
using Reviews.Application.Reviews.Commands.AddComment;
using Reviews.Application.Reviews.Commands.UpdateReview;
using Reviews.WebApi.Contracts;

namespace Reviews.WebApi.Mapping;

/// <summary>
/// HTTP-моделі → команди MediatR. Поля з маршруту й заголовків (Id, версія) контролер дописує через <c>with</c>.
/// </summary>
public sealed class RequestProfile : Profile
{
    public RequestProfile()
    {
        CreateMap<UpdateReviewRequest, UpdateReviewCommand>()
            .ForCtorParam(nameof(UpdateReviewCommand.Id), o => o.MapFrom(_ => string.Empty))
            .ForCtorParam(nameof(UpdateReviewCommand.ExpectedVersion), o => o.MapFrom(_ => 0));

        CreateMap<AddCommentRequest, AddCommentCommand>()
            .ForCtorParam(nameof(AddCommentCommand.ReviewId), o => o.MapFrom(_ => string.Empty));

        CreateMap<PostMessageRequest, PostMessageCommand>()
            .ForCtorParam(nameof(PostMessageCommand.DiscussionId), o => o.MapFrom(_ => string.Empty));
    }
}
