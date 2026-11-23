// Оркестрація .NET Aspire (Лаб. 05–06): одна команда піднімає всю платформу —
// контейнери SQL Server, MongoDB і Redis з постійними томами, три мікросервіси (Лаб. 02–04), Aggregator і API Gateway.
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

// ---------- Лаб. 06: Redis — L2-кеш (спільний для реплік) і кеш агрегованих відповідей ----------
// Сервіси отримують ConnectionStrings:redis через WithReference.
var redis = builder.AddRedis("redis")
                   .WithDataVolume("cinema-redis-data");

// ---------- Мікросервіси (Лаб. 02–04). Ім'я ресурсу = логічне ім'я для service discovery ----------
// Лаб. 06: кожен сервіс має два endpoint-и в Kestrel:Endpoints свого appsettings.json — "http" (REST через Gateway)
// і "grpc" з Protocols=Http2 (h2c без TLS для внутрішніх gRPC-викликів). Aspire бере їх з конфігурації сервісу
// й лише підставляє адреси; агрегатор звертається до http://_grpc.<сервіс>.
var booking = builder.AddProject<Projects.Booking_Api>("booking-api")
                     .WithReference(bookingDb)
                     .WaitFor(bookingDb)
                     .WithUrlForEndpoint("http", url => url.Url = "/swagger");

var catalog = builder.AddProject<Projects.Catalog_Api>("catalog-api")
                     .WithReference(catalogDb).WaitFor(catalogDb)
                     .WithReference(redis).WaitFor(redis)
                     .WithUrlForEndpoint("http", url => url.Url = "/swagger");

var reviews = builder.AddProject<Projects.Reviews_WebApi>("reviews-api")
                     .WithReference(reviewsDb).WaitFor(reviewsDb)
                     .WithReference(redis).WaitFor(redis)
                     .WithUrlForEndpoint("http", url => url.Url = "/swagger");

// ---------- Aggregator: gRPC-клієнт трьох сервісів + кеш агрегатів у Redis; стартує після них ----------
var aggregator = builder.AddProject<Projects.Platform_Aggregator>("aggregator")
                        .WithReference(booking).WithReference(catalog).WithReference(reviews)
                        .WithReference(redis).WaitFor(redis)
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
