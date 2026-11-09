using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using Reviews.Application.Common.Diagnostics;
using Reviews.Application.Common.Messaging;
using Reviews.Domain.Entities;
using Reviews.Domain.Interfaces;
using Reviews.Domain.ValueObjects;

namespace Reviews.Application.Discussions.Commands.CreateDiscussion;

/// <summary>Створити тему обговорення фільму. Результат — Id (рядок ObjectId).</summary>
public sealed record CreateDiscussionCommand(
    int MovieId,
    string MovieTitle,
    string Title,
    int AuthorId,
    string AuthorName,
    IReadOnlyList<string>? Tags = null) : ICommand<string>;

public sealed class CreateDiscussionCommandHandler(IDiscussionRepository discussions, ILogger<CreateDiscussionCommandHandler> logger)
    : IRequestHandler<CreateDiscussionCommand, string>
{
    public async Task<string> Handle(CreateDiscussionCommand cmd, CancellationToken ct)
    {
        using var activity = ReviewsTelemetry.Source.StartActivity("discussion.create");
        activity?.SetTag("movie.id", cmd.MovieId);
        var discussion = new Discussion(cmd.MovieId, cmd.MovieTitle, cmd.Title,
            new Author(cmd.AuthorId, cmd.AuthorName), cmd.Tags);
        await discussions.AddAsync(discussion, ct);
        logger.LogInformation("Створено тему {DiscussionId} фільму {MovieId}, автор {CustomerId}, тегів {TagsCount}",
            discussion.Id.ToString(), discussion.MovieId, discussion.Author.CustomerId, discussion.Tags.Count);
        return discussion.Id.ToString();
    }
}

public sealed class CreateDiscussionCommandValidator : AbstractValidator<CreateDiscussionCommand>
{
    public CreateDiscussionCommandValidator()
    {
        RuleFor(x => x.MovieId).GreaterThan(0);
        RuleFor(x => x.MovieTitle).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Title).NotEmpty().Length(Discussion.TitleMinLength, Discussion.TitleMaxLength);
        RuleFor(x => x.AuthorId).GreaterThan(0);
        RuleFor(x => x.AuthorName).NotEmpty().MaximumLength(Author.DisplayNameMaxLength);
        RuleFor(x => x.Tags).Must(t => t is null || t.Count <= Discussion.MaxTags)
            .WithMessage($"Не більше {Discussion.MaxTags} тегів.");
        RuleForEach(x => x.Tags).NotEmpty().MaximumLength(Discussion.TagMaxLength);
    }
}
