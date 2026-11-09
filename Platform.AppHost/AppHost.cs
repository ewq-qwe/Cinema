// Оркестрація .NET Aspire (Лаб. 05): одна команда піднімає всю платформу —
// контейнери SQL Server і MongoDB з постійними томами, три мікросервіси (Лаб. 02–04), Aggregator і API Gateway.
// AppHost не містить бізнес-логіки: лише ресурси, зв'язки (WithReference) і порядок запуску (WaitFor).
//   dotnet run --project Platform.AppHost
var builder = DistributedApplication.CreateBuilder(args);

// ---------- Бази даних ----------
// Пароль SQL Server — параметр-секрет: user-secrets AppHost (Parameters:sql-password) або ввести в Dashboard.
// Том зберігає і дані, і пароль sa, тому пароль має бути тим самим між запусками.
var sqlPassword = builder.AddParameter("sql-password", secret: true);
var sql = builder.AddSqlServer("sql", sqlPassword)
                 .WithDataVolume("cinema-sql-data");

// CinemaBooking (Лаб. 02) — схема, процедури й дані зі скриптів Лаб. 01 (db/p1).
// schema.sql створює БД з нуля, тому з томом виконуємо скрипти лише при першому створенні:
// якщо БД уже існує, SET NOEXEC ON лише компілює решту батчів, нічого не змінюючи.
var scriptsDir = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "db", "p1"));
var creationScript = string.Join(Environment.NewLine,
[
    "IF DB_ID(N'CinemaBooking') IS NOT NULL SET NOEXEC ON;",
    "GO",
    .. new[] { "schema.sql", "procedures.sql", "seed.sql", "security.sql" }
        .Select(file => File.ReadAllText(Path.Combine(scriptsDir, file))),
    "GO",
    "SET NOEXEC OFF;",
    "GO"
]);
// Імена ресурсів БД = імена рядків підключення, які сервіси вже читають (ConnectionStrings:BookingDb тощо)
var bookingDb = sql.AddDatabase("BookingDb", databaseName: "CinemaBooking").WithCreationScript(creationScript);
var catalogDb = sql.AddDatabase("CatalogDb", databaseName: "CinemaCatalog");   // схему створюють міграції EF Core

var mongo = builder.AddMongoDB("mongo")
                   .WithDataVolume("cinema-mongo-data");
var reviewsDb = mongo.AddDatabase("ReviewsDb", databaseName: "cinema_reviews");  // індекси й сідінг — на старті сервісу

// ---------- Мікросервіси (Лаб. 02–04). Ім'я ресурсу = логічне ім'я для service discovery ----------
var booking = builder.AddProject<Projects.Booking_Api>("booking-api")
                     .WithReference(bookingDb)
                     .WaitFor(bookingDb)
                     .WithUrlForEndpoint("http", url => url.Url = "/swagger");

var catalog = builder.AddProject<Projects.Catalog_Api>("catalog-api")
                     .WithReference(catalogDb)
                     .WaitFor(catalogDb)
                     .WithUrlForEndpoint("http", url => url.Url = "/swagger");

var reviews = builder.AddProject<Projects.Reviews_WebApi>("reviews-api")
                     .WithReference(reviewsDb)
                     .WaitFor(reviewsDb)
                     .WithUrlForEndpoint("http", url => url.Url = "/swagger");

// ---------- Aggregator: знає про три сервіси, стартує після них ----------
var aggregator = builder.AddProject<Projects.Platform_Aggregator>("aggregator")
                        .WithReference(booking).WithReference(catalog).WithReference(reviews)
                        .WaitFor(booking).WaitFor(catalog).WaitFor(reviews)
                        .WithUrlForEndpoint("http", url => url.Url = "/swagger");

// ---------- API Gateway: єдина точка входу на фіксованому порту 5000 ----------
// Порядок запуску: БД → сервіси → Aggregator → Gateway
builder.AddProject<Projects.Platform_ApiGateway>("gateway")
       .WithHttpEndpoint(port: 5000, name: "public")
       .WithReference(booking).WithReference(catalog).WithReference(reviews)
       .WithReference(aggregator)
       .WaitFor(aggregator);

builder.Build().Run();
