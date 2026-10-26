using AutoMapper;
using Microsoft.Extensions.Logging.Abstractions;
using Reviews.Application.Common.Mapping;
using Reviews.Application.Common.Models;
using Reviews.Application.Reviews.Commands.UpdateReview;
using Reviews.Domain.Entities;
using Reviews.Domain.Enums;
using Reviews.Domain.ValueObjects;
using Reviews.WebApi.Contracts;
using Reviews.WebApi.Mapping;

namespace Reviews.Tests;

public class MappingTests
{
    private static readonly MapperConfiguration Config = new(cfg =>
    {
        cfg.AddMaps(typeof(ReviewProfile).Assembly, typeof(RequestProfile).Assembly);
    }, NullLoggerFactory.Instance);

    private readonly IMapper _mapper = Config.CreateMapper();

    [Fact]
    public void Configuration_is_valid() => Config.AssertConfigurationIsValid();

    [Fact]
    public void Review_maps_ObjectId_value_objects_and_nested_arrays()
    {
        var review = new Review(1, "Карпатський експрес", new Author(1, "Олена К."), ReviewKind.Full, new Rating(5),
            "Супер", "Краєвиди");
        review.AddComment("Тарас М.", "Згоден!");

        var dto = _mapper.Map<ReviewDetailsDto>(review);

        Assert.Equal(review.Id.ToString(), dto.Id);
        Assert.Equal("Олена К.", dto.AuthorName);
        Assert.Equal(1, dto.AuthorId);
        Assert.Equal(5, dto.Rating);
        Assert.Equal("full", dto.Kind);
        Assert.Equal(1, dto.CommentsCount);
        Assert.Equal("Згоден!", dto.Comments.Single().Text);
        Assert.Null(dto.DiscussionId);
    }

    [Fact]
    public void Request_maps_to_command_and_route_values_are_added()
    {
        var command = _mapper.Map<UpdateReviewCommand>(new UpdateReviewRequest(4, "Заголовок", "Текст"))
            with { Id = "670000000000000000000001", ExpectedVersion = 2 };

        Assert.Equal(4, command.Rating);
        Assert.Equal("Текст", command.Text);
        Assert.Equal(2, command.ExpectedVersion);
    }
}
