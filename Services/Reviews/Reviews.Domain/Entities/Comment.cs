using Reviews.Domain.Exceptions;

namespace Reviews.Domain.Entities;

/// <summary>
/// Коментар під відгуком — вкладений документ масиву <c>comments[]</c> (embedding з Лаб. 01:
/// коментарів небагато і їх завжди читають разом із відгуком). Створюється лише через <see cref="Review.AddComment"/>.
/// </summary>
public class Comment
{
    public const int TextMaxLength = 500;

    public string Author { get; private set; } = "";
    public string Text { get; private set; } = "";
    public DateTime CreatedAt { get; private set; }

    private Comment() { } // для десеріалізації

    internal Comment(string author, string text, DateTime createdAt)
    {
        author = author?.Trim() ?? "";
        text = text?.Trim() ?? "";
        if (author.Length is 0 or > ValueObjects.Author.DisplayNameMaxLength)
            throw new DomainException("Вкажіть автора коментаря (до 60 символів).");
        if (text.Length is 0 or > TextMaxLength)
            throw new DomainException($"Текст коментаря обов'язковий і не довший за {TextMaxLength} символів.");

        Author = author;
        Text = text;
        CreatedAt = createdAt;
    }
}
