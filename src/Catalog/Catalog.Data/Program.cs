using Catalog.Data;
using Microsoft.EntityFrameworkCore;

// Застосовує міграції (створює CinemaCatalog із seed-даними) і виконує кілька перевірочних запитів.
// Повна реалізація сервісу на EF Core — у Лаб. 03.
var options = new DbContextOptionsBuilder<CatalogDbContext>()
    .UseSqlServer(Environment.GetEnvironmentVariable("CINEMA_CATALOG_DB") ?? CatalogDbContext.DefaultConnection)
    .Options;

await using var db = new CatalogDbContext(options);
await db.Database.MigrateAsync();
Console.WriteLine("Міграції: " + string.Join(", ", await db.Database.GetAppliedMigrationsAsync()));

// 1:N — фільми за жанрами
foreach (var g in await db.Genres.OrderBy(g => g.Name)
             .Select(g => new { g.Name, Movies = g.Movies.Select(m => m.Title).ToList() }).ToListAsync())
    Console.WriteLine($"{g.Name,-10}: {string.Join(", ", g.Movies)}");

// 1:1 + M:N — картка фільму з описом і командою
var movie = await db.Movies
    .Include(m => m.Detail)
    .Include(m => m.Credits.OrderBy(c => c.BillingOrder)).ThenInclude(c => c.Person)
    .SingleAsync(m => m.Id == 2);
Console.WriteLine($"\n{movie.Title} ({movie.ReleaseYear}, {movie.DurationMinutes} хв, {movie.AgeRating})");
Console.WriteLine(movie.Detail!.Description);
foreach (var c in movie.Credits)
    Console.WriteLine($"  {c.Role,-8} {c.Person.FullName}{(c.CharacterName is null ? "" : " — " + c.CharacterName)}");

// M:N у зворотний бік — фільмографія людини
var filmography = await db.MovieCredits
    .Where(c => c.Person.FullName == "Олег Кравець")
    .Select(c => c.Movie.Title + " (" + c.CharacterName + ")")
    .ToListAsync();
Console.WriteLine($"\nОлег Кравець: {string.Join("; ", filmography)}");
