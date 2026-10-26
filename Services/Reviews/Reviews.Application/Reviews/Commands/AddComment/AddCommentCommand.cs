using AutoMapper;
using FluentValidation;
using MediatR;
using MongoDB.Bson;
using Reviews.Application.Common.Messaging;
using Reviews.Application.Common.Models;
using Reviews.Application.Common.Validation;
using Reviews.Domain.Entities;
using Reviews.Domain.Exceptions;
using Reviews.Domain.Interfaces;
using Reviews.Domain.ValueObjects;

namespace Reviews.Application.Reviews.Commands.AddComment;

/// <summary>Додати коментар у вкладений масив comments[] відгуку.</summary>
public sealed record AddCommentCommand(string ReviewId, string Author, string Text) : ICommand<ReviewDetailsDto>;

public sealed class AddCommentCommandHandler(IReviewRepository reviews, IMapper mapper)
    : IRequestHandler<AddCommentCommand, ReviewDetailsDto>
{
    public async Task<ReviewDetailsDto> Handle(AddCommentCommand cmd, CancellationToken ct)
    {
        var review = await reviews.GetByIdAsync(ObjectId.Parse(cmd.ReviewId), ct)
                     ?? throw NotFoundException.For("Відгук", cmd.ReviewId);

        review.AddComment(cmd.Author, cmd.Text);  // правила (лише full, не більше 20) — у сутності
        await reviews.UpdateAsync(review, ct);    // з перевіркою версії
        return mapper.Map<ReviewDetailsDto>(review);
    }
}

public sealed class AddCommentCommandValidator : AbstractValidator<AddCommentCommand>
{
    public AddCommentCommandValidator()
    {
        RuleFor(x => x.ReviewId).MustBeObjectId();
        RuleFor(x => x.Author).NotEmpty().MaximumLength(Author.DisplayNameMaxLength);
        RuleFor(x => x.Text).NotEmpty().MaximumLength(Comment.TextMaxLength);
    }
}
