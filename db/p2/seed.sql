/* =====================================================================
   Проєкт №2. seed.sql — довідникові дані CinemaCatalog. ІДЕМПОТЕНТНИЙ:
   кожен рядок вставляється лише якщо його ще немає (INSERT ... WHERE NOT EXISTS).
   Дані ті самі, що в CatalogSeed.cs (HasData у моделі EF). Фільми й люди вигадані.
   Movies.Id 1–6 збігаються з dbo.Movies у CinemaBooking і movieId у MongoDB.
   ===================================================================== */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
USE CinemaCatalog;
GO
SET NOCOUNT ON;
DECLARE @At DATETIME2(3) = '2026-09-01T00:00:00';

/* ---------- Жанри ---------- */
SET IDENTITY_INSERT dbo.Genres ON;
INSERT dbo.Genres (Id, Name, CreatedAt, CreatedBy)
SELECT v.Id, v.Name, @At, N'seed'
FROM (VALUES (1, N'Пригоди'), (2, N'Детектив'), (3, N'Анімація'),
             (4, N'Драма'),   (5, N'Трилер'),   (6, N'Комедія')) AS v (Id, Name)
WHERE NOT EXISTS (SELECT 1 FROM dbo.Genres g WHERE g.Id = v.Id);
SET IDENTITY_INSERT dbo.Genres OFF;

/* ---------- Фільми ---------- */
SET IDENTITY_INSERT dbo.Movies ON;
INSERT dbo.Movies (Id, GenreId, Title, ReleaseYear, DurationMinutes, AgeRating, CreatedAt, CreatedBy)
SELECT v.Id, v.GenreId, v.Title, v.Year, v.Duration, v.Age, @At, N'seed'
FROM (VALUES
    (1, 1, N'Карпатський експрес',    2025, 118, '12+'),
    (2, 2, N'Таємниця старого маяка', 2024, 104, '12+'),
    (3, 3, N'Мандрівка до зірок',     2025,  92, '0+'),
    (4, 4, N'Остання зміна',          2023, 131, '16+'),
    (5, 5, N'Місто без світла',       2024, 109, '16+'),
    (6, 6, N'Кіт на ім''я Борщ',      2025,  88, '6+')
) AS v (Id, GenreId, Title, Year, Duration, Age)
WHERE NOT EXISTS (SELECT 1 FROM dbo.Movies m WHERE m.Id = v.Id);
SET IDENTITY_INSERT dbo.Movies OFF;

/* ---------- 1:1 Описи фільмів ---------- */
INSERT dbo.MovieDetails (MovieId, Description, Country, PosterUrl)
SELECT v.MovieId, v.Description, v.Country, CONCAT('https://cdn.cinema-kadr.example/posters/', v.MovieId, '.jpg')
FROM (VALUES
    (1, N'Потяг через гори, зниклий вагон із золотом і троє незнайомців, яким доведеться довіритися одне одному.', N'Україна'),
    (2, N'Молода слідча приїжджає в приморське містечко, де щоночі на покинутому маяку загоряється світло.', N'Україна'),
    (3, N'Хлопчик і його робот-пес будують ракету з картонних коробок — і вона справді злітає.', N'Україна'),
    (4, N'Одна нічна зміна в лікарні маленького міста, що змінює життя п''ятьох людей.', N'Україна, Польща'),
    (5, N'Місто занурюється в темряву, і хтось використовує це, щоб приховати злочин.', N'Україна'),
    (6, N'Рудий кіт випадково стає зіркою кулінарного шоу.', N'Україна')
) AS v (MovieId, Description, Country)
WHERE NOT EXISTS (SELECT 1 FROM dbo.MovieDetails d WHERE d.MovieId = v.MovieId);

/* ---------- Люди ---------- */
SET IDENTITY_INSERT dbo.People ON;
INSERT dbo.People (Id, FullName, BirthYear, CreatedAt, CreatedBy)
SELECT v.Id, v.FullName, v.BirthYear, @At, N'seed'
FROM (VALUES
    (1, N'Андрій Гнатюк', 1978), (2, N'Марта Савчук', 1995), (3, N'Олег Кравець', 1988),
    (4, N'Софія Лисенко', 2001), (5, N'Богдан Левицький', 1970), (6, N'Ярина Пасічник', 1992),
    (7, N'Дмитро Онищенко', 1983), (8, N'Наталія Руденко', 1990)
) AS v (Id, FullName, BirthYear)
WHERE NOT EXISTS (SELECT 1 FROM dbo.People p WHERE p.Id = v.Id);
SET IDENTITY_INSERT dbo.People OFF;

/* ---------- M:N Команда фільмів ---------- */
INSERT dbo.MovieCredits (MovieId, PersonId, Role, CharacterName, BillingOrder)
SELECT v.MovieId, v.PersonId, v.Role, v.CharacterName, v.BillingOrder
FROM (VALUES
    (1, 1, 'Director', NULL, 0), (1, 3, 'Actor', N'Мирон', 1), (1, 2, 'Actor', N'Зоряна', 2),
    (2, 5, 'Director', NULL, 0), (2, 6, 'Actor', N'слідча Ганна Ровенко', 1), (2, 3, 'Actor', N'доглядач маяка', 2),
    (3, 7, 'Director', NULL, 0), (3, 4, 'Actor', N'Тимко (голос)', 1),
    (4, 5, 'Director', NULL, 0), (4, 8, 'Actor', N'лікарка Віра', 1), (4, 2, 'Actor', N'медсестра Оля', 2),
    (5, 1, 'Director', NULL, 0), (5, 3, 'Actor', N'детектив Шевчук', 1), (5, 6, 'Actor', N'журналістка', 2),
    (6, 7, 'Director', NULL, 0), (6, 4, 'Actor', N'ведуча шоу', 1)
) AS v (MovieId, PersonId, Role, CharacterName, BillingOrder)
WHERE NOT EXISTS (SELECT 1 FROM dbo.MovieCredits c
                  WHERE c.MovieId = v.MovieId AND c.PersonId = v.PersonId AND c.Role = v.Role);
GO

/* ---------- Контроль: кількість записів (при повторному запуску не змінюється) ---------- */
SELECT (SELECT COUNT(*) FROM dbo.Genres)       AS Genres,
       (SELECT COUNT(*) FROM dbo.Movies)       AS Movies,
       (SELECT COUNT(*) FROM dbo.MovieDetails) AS MovieDetails,
       (SELECT COUNT(*) FROM dbo.People)       AS People,
       (SELECT COUNT(*) FROM dbo.MovieCredits) AS MovieCredits;
GO
