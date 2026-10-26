using AutoMapper;
using MongoDB.Bson;
using Reviews.Application.Common.Models;
using Reviews.Application.Common.Validation;
using Reviews.Domain.Entities;
using Reviews.Domain.ValueObjects;

namespace Reviews.Application.Common.Mapping;

/// <summary>
/// Сутності → DTO для результатів команд: ObjectId → string, Value Objects (Author, Rating, DiscussionStats)
/// розгортаються в плоскі поля, вкладені масиви (comments, lastMessages) мапляться поелементно.
/// </summary>
public sealed class ReviewProfile : Profile
{
    public ReviewProfile()
    {
        CreateMap<ObjectId, string>().ConvertUsing(id => id.ToString());

        CreateMap<Review, ReviewDto>()
            .ForMember(d => d.AuthorId, o => o.MapFrom(s => s.Author.CustomerId))
            .ForMember(d => d.AuthorName, o => o.MapFrom(s => s.Author.DisplayName))
            .ForMember(d => d.Kind, o => o.MapFrom(s => ReviewKinds.ToName(s.Kind)))
            .ForMember(d => d.Rating, o => o.MapFrom(s => s.Rating.Value))
            .ForMember(d => d.CommentsCount, o => o.MapFrom(s => s.Comments.Count));

        CreateMap<Review, ReviewDetailsDto>()
            .IncludeBase<Review, ReviewDto>()
            .ForMember(d => d.DiscussionId, o => o.MapFrom(s => s.DiscussionId.HasValue ? s.DiscussionId.Value.ToString() : null));

        CreateMap<Comment, CommentDto>();
    }
}

public sealed class DiscussionProfile : Profile
{
    public DiscussionProfile()
    {
        CreateMap<Discussion, DiscussionDto>()
            .ForMember(d => d.AuthorId, o => o.MapFrom(s => s.Author.CustomerId))
            .ForMember(d => d.AuthorName, o => o.MapFrom(s => s.Author.DisplayName))
            .ForMember(d => d.MessagesCount, o => o.MapFrom(s => s.Stats.MessagesCount))
            .ForMember(d => d.LastActivityAt, o => o.MapFrom(s => s.Stats.LastActivityAt));

        CreateMap<MessagePreview, MessagePreviewDto>();

        CreateMap<Message, MessageDto>()
            .ForMember(d => d.AuthorId, o => o.MapFrom(s => s.Author.CustomerId))
            .ForMember(d => d.AuthorName, o => o.MapFrom(s => s.Author.DisplayName))
            .ForMember(d => d.ReplyTo, o => o.MapFrom(s => s.ReplyTo.HasValue ? s.ReplyTo.Value.ToString() : null));
    }
}
