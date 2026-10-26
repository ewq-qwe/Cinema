using Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Catalog.Dal.Seeding;

/// <summary>
/// Програмний сідінг — виконується після MigrateAsync() (див. Program.cs Catalog.Api).
/// Довідник жанрів (фіксовані Id) засіюється статично через HasData у GenreConfiguration.
/// ІДЕМПОТЕНТНИЙ: кожна група даних вставляється лише якщо її ще немає — повторний запуск дублів не створює.
/// Порядок — з урахуванням FK: жанри (міграція) → люди → фільми з описами й титрами.
/// Фільми й люди вигадані; на порожній БД фільми отримують Id 1–6 — ті самі, що в копії довідника CinemaBooking.
/// </summary>
public static class DbInitializer
{
    public static async Task SeedAsync(CatalogDbContext db, ILogger logger, CancellationToken ct = default)
    {
        if (await db.Movies.IgnoreQueryFilters().AnyAsync(ct))
        {
            logger.LogInformation("Seeding: дані каталогу вже є — пропущено");
            return;
        }

        var people = new Dictionary<string, Person>();
        foreach (var (name, year) in new (string, int)[]
                 {
                     ("Андрій Гнатюк", 1978), ("Марта Савчук", 1995), ("Олег Кравець", 1988), ("Софія Лисенко", 2001),
                     ("Богдан Левицький", 1970), ("Ярина Пасічник", 1992), ("Дмитро Онищенко", 1983), ("Наталія Руденко", 1990)
                 })
        {
            var person = await db.People.FirstOrDefaultAsync(p => p.FullName == name, ct) ?? new Person(name, year);
            if (person.Id == 0) db.People.Add(person);
            people[name] = person;
        }

        var posterNo = 0;
        Movie M(string title, int year, int minutes, string age, int genreId, string description, string country)
        {
            var movie = new Movie(title, year, minutes, age, genreId);
            movie.SetDetail(description, country, null, $"https://cdn.cinema-kadr.example/posters/{++posterNo}.jpg");
            db.Movies.Add(movie);
            return movie;
        }

        var express = M("Карпатський експрес", 2025, 118, "12+", 1,
            "Потяг через гори, зниклий вагон із золотом і троє незнайомців, яким доведеться довіритися одне одному.", "Україна");
        var lighthouse = M("Таємниця старого маяка", 2024, 104, "12+", 2,
            "Молода слідча приїжджає в приморське містечко, де щоночі на покинутому маяку загоряється світло.", "Україна");
        var stars = M("Мандрівка до зірок", 2025, 92, "0+", 3,
            "Хлопчик і його робот-пес будують ракету з картонних коробок — і вона справді злітає.", "Україна");
        var shift = M("Остання зміна", 2023, 131, "16+", 4,
            "Одна нічна зміна в лікарні маленького міста, що змінює життя п'ятьох людей.", "Україна, Польща");
        var dark = M("Місто без світла", 2024, 109, "16+", 5,
            "Місто занурюється в темряву, і хтось використовує це, щоб приховати злочин.", "Україна");
        var cat = M("Кіт на ім'я Борщ", 2025, 88, "6+", 6,
            "Рудий кіт випадково стає зіркою кулінарного шоу.", "Україна");

        express.AddCredit(people["Андрій Гнатюк"], CreditRole.Director, null, 0);
        express.AddCredit(people["Олег Кравець"], CreditRole.Actor, "Мирон", 1);
        express.AddCredit(people["Марта Савчук"], CreditRole.Actor, "Зоряна", 2);
        lighthouse.AddCredit(people["Богдан Левицький"], CreditRole.Director, null, 0);
        lighthouse.AddCredit(people["Ярина Пасічник"], CreditRole.Actor, "слідча Ганна Ровенко", 1);
        lighthouse.AddCredit(people["Олег Кравець"], CreditRole.Actor, "доглядач маяка", 2);
        stars.AddCredit(people["Дмитро Онищенко"], CreditRole.Director, null, 0);
        stars.AddCredit(people["Софія Лисенко"], CreditRole.Actor, "Тимко (голос)", 1);
        shift.AddCredit(people["Богдан Левицький"], CreditRole.Director, null, 0);
        shift.AddCredit(people["Наталія Руденко"], CreditRole.Actor, "лікарка Віра", 1);
        shift.AddCredit(people["Марта Савчук"], CreditRole.Actor, "медсестра Оля", 2);
        dark.AddCredit(people["Андрій Гнатюк"], CreditRole.Director, null, 0);
        dark.AddCredit(people["Олег Кравець"], CreditRole.Actor, "детектив Шевчук", 1);
        dark.AddCredit(people["Ярина Пасічник"], CreditRole.Actor, "журналістка", 2);
        cat.AddCredit(people["Дмитро Онищенко"], CreditRole.Director, null, 0);
        cat.AddCredit(people["Софія Лисенко"], CreditRole.Actor, "ведуча шоу", 1);

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeding: додано {Movies} фільмів і {People} людей", 6, people.Count);
    }

}
