using Catalog.Domain.Entities;
using Catalog.Domain.Exceptions;

namespace Catalog.Tests;

/// <summary>Бізнес-правила живуть у сутностях: конструктор валідує, стан змінюють лише методи.</summary>
public class DomainRulesTests
{
    private static Movie NewMovie() => new("Карпатський експрес", 2025, 118, "12+", 1);

    [Theory]
    [InlineData("", 2025, 100, "12+")]
    [InlineData("Фільм", 1800, 100, "12+")]
    [InlineData("Фільм", 2025, 0, "12+")]
    [InlineData("Фільм", 2025, 100, "21+")]
    public void Movie_constructor_rejects_invalid_data(string title, int year, int minutes, string age) =>
        Assert.Throws<DomainException>(() => new Movie(title, year, minutes, age, 1));

    [Fact]
    public void AddCredit_rejects_same_person_in_same_role_twice()
    {
        var movie = NewMovie();
        var actor = new Person("Олег Кравець", 1988);
        movie.AddCredit(actor, CreditRole.Actor, "Мирон", 1);

        Assert.Throws<DomainException>(() => movie.AddCredit(actor, CreditRole.Actor, "Інший", 2));
    }

    [Fact]
    public void AddCredit_accepts_several_new_unsaved_people()
    {
        // Регресія: у нових (не збережених) людей Id = 0 — це не повинно вважатися дублем
        var movie = NewMovie();
        movie.AddCredit(new Person("Андрій Гнатюк", 1978), CreditRole.Director, null, 0);
        movie.AddCredit(new Person("Олег Кравець", 1988), CreditRole.Actor, "Мирон", 1);
        movie.AddCredit(new Person("Марта Савчук", 1995), CreditRole.Actor, "Зоряна", 2);

        Assert.Equal(3, movie.Credits.Count);
    }

    [Fact]
    public void AddCredit_requires_character_for_actor_and_forbids_it_for_director()
    {
        var movie = NewMovie();
        Assert.Throws<DomainException>(() => movie.AddCredit(new Person("Актор", 1990), CreditRole.Actor, null, 1));
        Assert.Throws<DomainException>(() => movie.AddCredit(new Person("Режисер", 1970), CreditRole.Director, "Роль", 0));
    }

    [Fact]
    public void AddCredit_rejects_taken_billing_position()
    {
        var movie = NewMovie();
        movie.AddCredit(new Person("Перший", 1990), CreditRole.Actor, "А", 1);
        Assert.Throws<DomainException>(() => movie.AddCredit(new Person("Другий", 1991), CreditRole.Actor, "Б", 1));
    }

    [Fact]
    public void Archived_movie_cannot_be_changed()
    {
        var movie = NewMovie();
        movie.Archive();
        Assert.True(movie.IsArchived);
        Assert.Throws<DomainException>(() => movie.ChangeDuration(120));
        Assert.Throws<DomainException>(() => movie.Archive());
    }

    [Fact]
    public void SetDetail_creates_then_updates_one_to_one_detail()
    {
        var movie = NewMovie();
        movie.SetDetail("Опис", "Україна", null, null, 1_500_000m);
        var detail = movie.Detail;
        movie.SetDetail("Новий опис", "Польща", "https://example.com/t", null);

        Assert.Same(detail, movie.Detail);
        Assert.Equal("Новий опис", movie.Detail!.Description);
        Assert.Throws<DomainException>(() => movie.SetDetail("Опис", "Україна", null, null, -5m));
    }
}
