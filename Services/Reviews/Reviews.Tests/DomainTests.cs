using MongoDB.Bson;
using Reviews.Domain.Entities;
using Reviews.Domain.Enums;
using Reviews.Domain.Exceptions;
using Reviews.Domain.ValueObjects;

namespace Reviews.Tests;

/// <summary>Правила домену перевіряються без жодного звернення до БД.</summary>
public class DomainTests
{
    private static readonly Author Olena = new(1, "Олена К.");

    private static Review FullReview() =>
        new(1, "Карпатський експрес", Olena, ReviewKind.Full, new Rating(5), "Супер", "Неймовірні краєвиди");

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(10)]
    public void Rating_out_of_range_throws_DomainException(int value) =>
        Assert.Throws<DomainException>(() => new Rating(value));

    [Fact]
    public void Creating_review_with_rating_10_throws_without_database() =>
        Assert.Throws<DomainException>(() =>
            new Review(1, "Карпатський експрес", Olena, ReviewKind.Quick, new Rating(10)));

    [Fact]
    public void Value_objects_are_equal_by_components()
    {
        Assert.Equal(new Author(1, "Олена К."), new Author(1, "Олена К."));
        Assert.NotEqual(new Author(1, "Олена К."), new Author(2, "Олена К."));
        Assert.True(new Rating(4) == new Rating(4));
        Assert.Equal(new Author(1, "Олена К.").GetHashCode(), new Author(1, "Олена К.").GetHashCode());
    }

    [Fact]
    public void Quick_review_contains_only_rating()
    {
        Assert.Throws<DomainException>(() =>
            new Review(1, "Фільм", Olena, ReviewKind.Quick, new Rating(4), text: "текст"));
        var quick = new Review(1, "Фільм", Olena, ReviewKind.Quick, new Rating(4));
        Assert.Null(quick.HasSpoilers);
        Assert.Throws<DomainException>(() => quick.AddComment("Тарас М.", "Коментар"));
    }

    [Fact]
    public void Critic_review_requires_publication()
    {
        Assert.Throws<DomainException>(() =>
            new Review(4, "Остання зміна", Olena, ReviewKind.Critic, new Rating(3), text: "Рецензія"));
        var critic = new Review(4, "Остання зміна", Olena, ReviewKind.Critic, new Rating(3),
            text: "Рецензія", publication: "Блог", pros: ["гра"], cons: ["темп"]);
        Assert.Equal(["гра"], critic.Pros);
    }

    [Fact]
    public void Comments_are_added_to_nested_array_and_limited()
    {
        var review = FullReview();
        for (var i = 0; i < Review.MaxComments; i++)
            review.AddComment("Тарас М.", $"Коментар {i}");

        Assert.Equal(Review.MaxComments, review.Comments.Count);
        Assert.Throws<DomainException>(() => review.AddComment("Тарас М.", "Зайвий"));
    }

    [Fact]
    public void Edit_cannot_break_kind_rules()
    {
        var review = FullReview();
        Assert.Throws<DomainException>(() =>
            review.Edit(new Rating(4), null, null, false, null, null, null, null));   // повний відгук без тексту
        review.Edit(new Rating(4), "Новий", "Новий текст", true, null, null, null, null);
        Assert.Equal(4, review.Rating.Value);
        Assert.NotNull(review.UpdatedAt);
    }

    [Fact]
    public void Discussion_keeps_only_three_last_messages_and_counts_all()
    {
        var discussion = new Discussion(1, "Карпатський експрес", "Де знімали?", Olena, ["Локації", "локації"]);
        Assert.Equal(["локації"], discussion.Tags);

        for (var i = 1; i <= 5; i++)
            discussion.RegisterMessage(new Message(discussion.Id, new Author(2, "Тарас М."), $"Повідомлення {i}"));

        Assert.Equal(5, discussion.Stats.MessagesCount);
        Assert.Equal(Discussion.LastMessagesLimit, discussion.LastMessages.Count);
        Assert.Equal("Повідомлення 5", discussion.LastMessages.Last().Excerpt);
    }

    [Fact]
    public void Message_from_other_discussion_is_rejected()
    {
        var discussion = new Discussion(1, "Фільм", "Тема", Olena);
        var foreign = new Message(ObjectId.GenerateNewId(), Olena, "Привіт");
        Assert.Throws<DomainException>(() => discussion.RegisterMessage(foreign));
    }

    [Fact]
    public void BumpVersion_increments_version()
    {
        var review = FullReview();
        review.BumpVersion();
        Assert.Equal(1, review.Version);
    }
}
