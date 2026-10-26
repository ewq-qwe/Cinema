using AutoMapper;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using Reviews.Application.Common.Messaging;
using Reviews.Application.Common.Models;
using Reviews.Application.Common.Validation;
using Reviews.Domain.Entities;
using Reviews.Domain.Exceptions;
using Reviews.Domain.Interfaces;
using Reviews.Domain.ValueObjects;

namespace Reviews.Application.Discussions.Commands.PostMessage;

/// <summary>Нове повідомлення в обговоренні (документ messages + оновлення lastMessages/stats теми).</summary>
public sealed record PostMessageCommand(
    string DiscussionId,
    int AuthorId,
    string AuthorName,
    string Text,
    string? ReplyTo = null) : ICommand<MessageDto>;

/// <summary>
/// Дві колекції без транзакції (локальний MongoDB — не replica set): спершу зберігається саме повідомлення
/// (джерело правди), потім денормалізоване прев'ю в темі. Якщо тему паралельно змінили (конфлікт версій),
/// її перечитують і повторюють — eventual consistency без втрати повідомлень.
/// </summary>
public sealed class PostMessageCommandHandler(
    IDiscussionRepository discussions,
    IMessageRepository messages,
    IMapper mapper,
    ILogger<PostMessageCommandHandler> logger) : IRequestHandler<PostMessageCommand, MessageDto>
{
    private const int MaxAttempts = 3;

    public async Task<MessageDto> Handle(PostMessageCommand cmd, CancellationToken ct)
    {
        var discussionId = ObjectId.Parse(cmd.DiscussionId);
        var discussion = await discussions.GetByIdAsync(discussionId, ct)
                         ?? throw NotFoundException.For("Обговорення", cmd.DiscussionId);

        var message = new Message(discussionId, new Author(cmd.AuthorId, cmd.AuthorName), cmd.Text,
            cmd.ReplyTo is null ? null : ObjectId.Parse(cmd.ReplyTo));
        await messages.AddAsync(message, ct);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                discussion.RegisterMessage(message);
                await discussions.UpdateAsync(discussion, ct);
                break;
            }
            catch (ConflictException) when (attempt < MaxAttempts)
            {
                logger.LogInformation("Тему {DiscussionId} змінено паралельно — спроба {Attempt} оновити прев'ю", discussionId, attempt + 1);
                discussion = await discussions.GetByIdAsync(discussionId, ct)
                             ?? throw NotFoundException.For("Обговорення", cmd.DiscussionId);
            }
        }

        return mapper.Map<MessageDto>(message);
    }
}

public sealed class PostMessageCommandValidator : AbstractValidator<PostMessageCommand>
{
    public PostMessageCommandValidator(IMessageRepository messages)
    {
        RuleFor(x => x.DiscussionId).MustBeObjectId();
        RuleFor(x => x.AuthorId).GreaterThan(0);
        RuleFor(x => x.AuthorName).NotEmpty().MaximumLength(Author.DisplayNameMaxLength);
        RuleFor(x => x.Text).NotEmpty().MaximumLength(Message.TextMaxLength);
        RuleFor(x => x.ReplyTo).MustBeObjectIdIfPresent();

        // Асинхронне правило з MongoDB: відповідати можна лише на повідомлення цього ж обговорення
        RuleFor(x => x.ReplyTo)
            .MustAsync(async (cmd, replyTo, ct) =>
                await messages.ExistsInDiscussionAsync(ObjectId.Parse(replyTo!), ObjectId.Parse(cmd.DiscussionId), ct))
            .WithMessage("Повідомлення, на яке ви відповідаєте, не знайдено в цьому обговоренні.")
            .When(x => ObjectId.TryParse(x.ReplyTo, out _) && ObjectId.TryParse(x.DiscussionId, out _));
    }
}
