// Оркестрація .NET Aspire: одна команда піднімає SQL Server у контейнері, створює БД CinemaBooking
// зі скриптів Лаб. 01 (db/p1) і CinemaCatalog (міграції EF Core), запускає Booking.Api і Catalog.Api.
//   dotnet run --project Platform.AppHost
var builder = DistributedApplication.CreateBuilder(args);

// Скрипти Проєкту №1 з Лаб. 01 — та сама схема, процедури й тестові дані, що й для docker compose
var scriptsDir = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "db", "p1"));
var creationScript = string.Join(Environment.NewLine,
    new[] { "schema.sql", "procedures.sql", "seed.sql", "security.sql" }
        .Select(file => File.ReadAllText(Path.Combine(scriptsDir, file))));

// SQL Server у контейнері. Без томи даних: при кожному запуску БД створюється заново зі скриптів,
// тож тестові сеанси завжди «завтра», а демо починається з відомого стану.
var sql = builder.AddSqlServer("sql");

// Ім'я ресурсу "BookingDb" = ім'я рядка підключення в appsettings.json Booking.Api,
// тому Aspire передає його в API як ConnectionStrings__BookingDb і код API змінювати не треба.
var bookingDb = sql.AddDatabase("BookingDb", databaseName: "CinemaBooking")
                   .WithCreationScript(creationScript);

builder.AddProject<Projects.Booking_Api>("booking-api")
       .WithReference(bookingDb)
       .WaitFor(bookingDb)
       .WithUrlForEndpoint("http", url => url.Url = "/swagger");

// Каталог (Лаб. 03, EF Core): порожня БД, схему створюють міграції, дані — сідінг (обидва — на старті Catalog.Api).
// Ресурс "CatalogDb" = ім'я рядка підключення в appsettings.json Catalog.Api.
var catalogDb = sql.AddDatabase("CatalogDb", databaseName: "CinemaCatalog");

builder.AddProject<Projects.Catalog_Api>("catalog-api")
       .WithReference(catalogDb)
       .WaitFor(catalogDb)
       .WithUrlForEndpoint("http", url => url.Url = "/swagger");

builder.Build().Run();
