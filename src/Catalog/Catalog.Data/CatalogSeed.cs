using Microsoft.EntityFrameworkCore;

namespace Catalog.Data;

/// <summary>
/// Початкові довідникові дані (HasData → потрапляють у міграцію; повторне застосування не дублює).
/// Фільми й люди вигадані. Movies.Id, назви, тривалість і віковий рейтинг збігаються
/// з локальною копією dbo.Movies у CinemaBooking та з movieId у MongoDB.
/// </summary>
internal static class CatalogSeed
{
    private static readonly DateTime At = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

    public static void Apply(ModelBuilder b)
    {
        b.Entity<Genre>().HasData(
            new Genre { Id = 1, Name = "Пригоди",  CreatedAt = At, CreatedBy = "seed" },
            new Genre { Id = 2, Name = "Детектив", CreatedAt = At, CreatedBy = "seed" },
            new Genre { Id = 3, Name = "Анімація", CreatedAt = At, CreatedBy = "seed" },
            new Genre { Id = 4, Name = "Драма",    CreatedAt = At, CreatedBy = "seed" },
            new Genre { Id = 5, Name = "Трилер",   CreatedAt = At, CreatedBy = "seed" },
            new Genre { Id = 6, Name = "Комедія",  CreatedAt = At, CreatedBy = "seed" });

        b.Entity<Movie>().HasData(
            M(1, 1, "Карпатський експрес",    2025, 118, "12+"),
            M(2, 2, "Таємниця старого маяка", 2024, 104, "12+"),
            M(3, 3, "Мандрівка до зірок",     2025,  92, "0+"),
            M(4, 4, "Остання зміна",          2023, 131, "16+"),
            M(5, 5, "Місто без світла",       2024, 109, "16+"),
            M(6, 6, "Кіт на ім'я Борщ",       2025,  88, "6+"));

        b.Entity<MovieDetail>().HasData(
            D(1, "Потяг через гори, зниклий вагон із золотом і троє незнайомців, яким доведеться довіритися одне одному.", "Україна"),
            D(2, "Молода слідча приїжджає в приморське містечко, де щоночі на покинутому маяку загоряється світло.", "Україна"),
            D(3, "Хлопчик і його робот-пес будують ракету з картонних коробок — і вона справді злітає.", "Україна"),
            D(4, "Одна нічна зміна в лікарні маленького міста, що змінює життя п'ятьох людей.", "Україна, Польща"),
            D(5, "Місто занурюється в темряву, і хтось використовує це, щоб приховати злочин.", "Україна"),
            D(6, "Рудий кіт випадково стає зіркою кулінарного шоу.", "Україна"));

        b.Entity<Person>().HasData(
            P(1, "Андрій Гнатюк", 1978), P(2, "Марта Савчук", 1995), P(3, "Олег Кравець", 1988),
            P(4, "Софія Лисенко", 2001), P(5, "Богдан Левицький", 1970), P(6, "Ярина Пасічник", 1992),
            P(7, "Дмитро Онищенко", 1983), P(8, "Наталія Руденко", 1990));

        b.Entity<MovieCredit>().HasData(
            C(1, 1, "Director", null, 0), C(1, 3, "Actor", "Мирон", 1), C(1, 2, "Actor", "Зоряна", 2),
            C(2, 5, "Director", null, 0), C(2, 6, "Actor", "слідча Ганна Ровенко", 1), C(2, 3, "Actor", "доглядач маяка", 2),
            C(3, 7, "Director", null, 0), C(3, 4, "Actor", "Тимко (голос)", 1),
            C(4, 5, "Director", null, 0), C(4, 8, "Actor", "лікарка Віра", 1), C(4, 2, "Actor", "медсестра Оля", 2),
            C(5, 1, "Director", null, 0), C(5, 3, "Actor", "детектив Шевчук", 1), C(5, 6, "Actor", "журналістка", 2),
            C(6, 7, "Director", null, 0), C(6, 4, "Actor", "ведуча шоу", 1));
    }

    private static Movie M(long id, int genreId, string title, int year, int duration, string age) => new()
    {
        Id = id, GenreId = genreId, Title = title, ReleaseYear = year, DurationMinutes = duration,
        AgeRating = age, CreatedAt = At, CreatedBy = "seed"
    };

    private static MovieDetail D(long movieId, string description, string country) => new()
    {
        MovieId = movieId, Description = description, Country = country,
        PosterUrl = $"https://cdn.cinema-kadr.example/posters/{movieId}.jpg"
    };

    private static Person P(int id, string name, int birthYear) =>
        new() { Id = id, FullName = name, BirthYear = birthYear, CreatedAt = At, CreatedBy = "seed" };

    private static MovieCredit C(long movieId, int personId, string role, string? character, int order) =>
        new() { MovieId = movieId, PersonId = personId, Role = role, CharacterName = character, BillingOrder = order };
}
