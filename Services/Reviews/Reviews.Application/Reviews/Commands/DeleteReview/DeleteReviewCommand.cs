using FluentValidation;
using MediatR;
using MongoDB.Bson;
using Reviews.Application.Common.Messaging;
using Reviews.Application.Common.Validation;
using Reviews.Domain.Exceptions;
using Reviews.Domain.Interfaces;

namespace Reviews.Application.Reviews.Commands.DeleteReview;

/// <summary>Видалити відгук. Якщо передано ExpectedVersion (If-Match) — лише цю версію документа.</summary>
public sealed record DeleteReviewCommand(string Id, int? ExpectedVersion = null) : ICommand;

public sealed class DeleteReviewCommandHandler(IReviewRepository reviews) : IRequestHandler<DeleteReviewCommand>
{
    public async Task Handle(DeleteReviewCommand cmd, CancellationToken ct)
    {
        var id = ObjectId.Parse(cmd.Id);
        var review = await reviews.GetByIdAsync(id, ct) ?? throw NotFoundException.For("Відгук", cmd.Id);
        await reviews.DeleteAsync(id, cmd.ExpectedVersion ?? review.Version, ct);
    }
}

public sealed class DeleteReviewCommandValidator : AbstractValidator<DeleteReviewCommand>
{
    public DeleteReviewCommandValidator()
    {
        RuleFor(x => x.Id).MustBeObjectId();
        RuleFor(x => x.ExpectedVersion).GreaterThanOrEqualTo(0);
    }
}
