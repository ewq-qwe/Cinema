namespace Reviews.WebApi.Contracts;

// Тіла HTTP-запитів, у яких частину даних команди дають маршрут (id) і заголовок (If-Match).
// У команди їх перетворює AutoMapper (RequestProfile).

/// <summary>Нові дані відгуку. Вид відгуку не змінюється; версія — у заголовку If-Match.</summary>
public sealed record UpdateReviewRequest(
    int Rating,
    string? Title = null,
    string? Text = null,
    bool HasSpoilers = false,
    IReadOnlyList<string>? Photos = null,
    string? Publication = null,
    IReadOnlyList<string>? Pros = null,
    IReadOnlyList<string>? Cons = null);

public sealed record AddCommentRequest(string Author, string Text);

public sealed record PostMessageRequest(int AuthorId, string AuthorName, string Text, string? ReplyTo = null);
