using Reviews.Application.Common.Messaging;
using Reviews.Application.Common.Models;

namespace Reviews.Application.Reviews.Commands.UpdateReview;

/// <summary>
/// Редагувати відгук. <see cref="ExpectedVersion"/> — версія, яку бачив клієнт (заголовок If-Match):
/// якщо документ уже змінили, зміни не застосовуються і повертається 409.
/// </summary>
public sealed record UpdateReviewCommand(
    string Id,
    int ExpectedVersion,
    int Rating,
    string? Title = null,
    string? Text = null,
    bool HasSpoilers = false,
    IReadOnlyList<string>? Photos = null,
    string? Publication = null,
    IReadOnlyList<string>? Pros = null,
    IReadOnlyList<string>? Cons = null) : ICommand<ReviewDetailsDto>;
