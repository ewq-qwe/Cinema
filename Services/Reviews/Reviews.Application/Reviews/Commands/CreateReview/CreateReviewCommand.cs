using Reviews.Application.Common.Messaging;

namespace Reviews.Application.Reviews.Commands.CreateReview;

/// <summary>Створити відгук. Результат — Id нового документа (рядок ObjectId).</summary>
public sealed record CreateReviewCommand(
    int MovieId,
    string MovieTitle,
    int AuthorId,
    string AuthorName,
    string Kind,
    int Rating,
    string? Title = null,
    string? Text = null,
    bool HasSpoilers = false,
    IReadOnlyList<string>? Photos = null,
    string? Publication = null,
    IReadOnlyList<string>? Pros = null,
    IReadOnlyList<string>? Cons = null) : ICommand<string>;
