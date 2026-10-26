namespace Reviews.Application.Common.Models;

// DTO — звичайні класи з init-властивостями: Infrastructure проєктує документи MongoDB одразу в них,
// а AutoMapper мапить у них доменні сутності (результати команд).

/// <summary>Відгук у списку (без масивів — лише кількість коментарів).</summary>
public class ReviewDto
{
    public string Id { get; init; } = "";
    public int MovieId { get; init; }
    public string MovieTitle { get; init; } = "";
    public int AuthorId { get; init; }
    public string AuthorName { get; init; } = "";
    public string Kind { get; init; } = "";
    public int Rating { get; init; }
    public string? Title { get; init; }
    public string? Text { get; init; }
    public bool? HasSpoilers { get; init; }
    public int Likes { get; init; }
    public int CommentsCount { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public int Version { get; init; }
}

/// <summary>Відгук повністю: вкладені коментарі, фото, плюси/мінуси, посилання на обговорення.</summary>
public class ReviewDetailsDto : ReviewDto
{
    public List<string> Photos { get; init; } = [];
    public string? Publication { get; init; }
    public List<string> Pros { get; init; } = [];
    public List<string> Cons { get; init; } = [];
    public string? DiscussionId { get; init; }
    public List<CommentDto> Comments { get; init; } = [];
}

public class CommentDto
{
    public string Author { get; init; } = "";
    public string Text { get; init; } = "";
    public DateTime CreatedAt { get; init; }
}

/// <summary>Рейтинг фільму, порахований aggregation pipeline ($match → $group).</summary>
public sealed record MovieRatingDto(int MovieId, string MovieTitle, double Average, int Count, IReadOnlyDictionary<int, int> Distribution);

public class DiscussionDto
{
    public string Id { get; init; } = "";
    public int MovieId { get; init; }
    public string MovieTitle { get; init; } = "";
    public string Title { get; init; } = "";
    public int AuthorId { get; init; }
    public string AuthorName { get; init; } = "";
    public List<string> Tags { get; init; } = [];
    public bool IsPinned { get; init; }
    public int MessagesCount { get; init; }
    public DateTime? LastActivityAt { get; init; }
    public List<MessagePreviewDto> LastMessages { get; init; } = [];
    public DateTime CreatedAt { get; init; }
    public int Version { get; init; }
}

public class MessagePreviewDto
{
    public string MessageId { get; init; } = "";
    public string Author { get; init; } = "";
    public string Excerpt { get; init; } = "";
    public DateTime CreatedAt { get; init; }
}

public class MessageDto
{
    public string Id { get; init; } = "";
    public string DiscussionId { get; init; } = "";
    public int AuthorId { get; init; }
    public string AuthorName { get; init; } = "";
    public string Text { get; init; } = "";
    public string? ReplyTo { get; init; }
    public DateTime CreatedAt { get; init; }
}
