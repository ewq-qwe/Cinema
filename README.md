# Кінотеатр «Кадр» — бази даних для мікросервісної архітектури

Практичне заняття № 01 «ASP.NET. Проєктування та розробка баз даних для мікросервісної архітектури».

**Домен.** Кінотеатр «Кадр» (вигаданий, 2 зали) продає квитки онлайн. Глядачі дивляться афішу, бронюють і оплачують місця на сеанс, а після перегляду пишуть відгуки й обговорюють фільми. Адміністратор веде каталог фільмів і розклад.

| № | Контекст (мікросервіс) | БД | Технологія | Сутності / колекції |
|---|---|---|---|---|
| 1 | **Бронювання квитків** | `CinemaBooking` | SQL Server + процедури (ADO.NET & Dapper — Лаб. 02) | Customers, Movies (копія), Halls, Seats, Sessions, Bookings, BookingPayments, Tickets |
| 2 | **Каталог фільмів** | `CinemaCatalog` | SQL Server + EF Core Code First | Genres, Movies, MovieDetails, People, MovieCredits |
| 3 | **Відгуки та обговорення** | `cinema_reviews` | MongoDB | reviews, discussions, messages, movieRatings |

Між базами немає жодного FOREIGN KEY: посилання на чужу сутність — звичайне поле з її Id. Детально: [docs/01-domain-and-contexts.md](docs/01-domain-and-contexts.md).

## Що де лежить

| Артефакт | Файл |
|---|---|
| Опис домену, контексти, агрегати, вибір ключів | [docs/01-domain-and-contexts.md](docs/01-domain-and-contexts.md) |
| Проєкт №1: ERD, зв'язки, індекси, процедури | [docs/02-booking-db.md](docs/02-booking-db.md), [docs/erd/booking.dbml](docs/erd/booking.dbml) |
| Проєкт №1: SQL | [`001_create_database.sql`](src/Booking/db/001_create_database.sql) · [`002_schema.sql`](src/Booking/db/002_schema.sql) · [`003_procedures.sql`](src/Booking/db/003_procedures.sql) · [`004_seed.sql`](src/Booking/db/004_seed.sql) · [`005_security.sql`](src/Booking/db/005_security.sql) · [`006_verify.sql`](src/Booking/db/006_verify.sql) |
| Проєкт №2: ERD, Fluent API, seed, міграції | [docs/03-catalog-db.md](docs/03-catalog-db.md), [docs/erd/catalog.dbml](docs/erd/catalog.dbml) |
| Проєкт №2: код і DDL | [`src/Catalog/Catalog.Data`](src/Catalog/Catalog.Data), [`catalog_schema.sql`](src/Catalog/db/catalog_schema.sql), [`catalog_security.sql`](src/Catalog/db/catalog_security.sql) |
| Проєкт №3: колекції, моделювання, запити | [docs/04-reviews-db.md](docs/04-reviews-db.md) |
| Проєкт №3: скрипти і JSON-приклади | [`src/Reviews/db`](src/Reviews/db) (01–04), [`src/Reviews/samples`](src/Reviews/samples) |

ERD у форматі dbdiagram.io: відкрийте https://dbdiagram.io/d і вставте вміст `.dbml`-файлу.

## Політика дублювання даних

| Поле | Де зберігається | Джерело (власник) | Політика узгодженості |
|---|---|---|---|
| `Movies.Title`, `DurationMinutes`, `AgeRating` | CinemaBooking (локальна копія довідника) | `Movies` у CinemaCatalog | **копія**, оновлюється подіями з Каталогу (механізм — Лаб. 08) |
| `Bookings.MovieTitle`, `Bookings.SessionStartsAt` | CinemaBooking | `Movies` (копія) / `Sessions` | **знімок** на момент бронювання — не оновлюється (це те, що надруковано на квитку) |
| `Tickets.Price` | CinemaBooking | `Sessions.BasePrice` × тип місця | **знімок** ціни на момент бронювання — не оновлюється |
| `reviews.movieTitle`, `discussions.movieTitle`, `movieRatings.movieTitle` | cinema_reviews | `Movies` у CinemaCatalog | **копія**, оновлюється подіями з Каталогу (запит 9 у `04_queries.js` показує оновлення) |
| `reviews.author.customerId`, `messages.author.customerId` | cinema_reviews | `Customers.Id` у CinemaBooking | лише **ідентифікатор**, без FK |
| `reviews.author.displayName`, `comments[].author` | cinema_reviews | `Customers.FullName` у CinemaBooking | **знімок** підпису на момент публікації — не оновлюється |
| `movieRatings.average`, `count`, `distribution` | cinema_reviews | `reviews` (та сама БД) | **проєкція**, перераховується агрегацією `$merge` |

Немає даних, які незалежно редагуються в двох сервісах (dual write). Змінити назву фільму може лише Каталог.

## Розгортання з нуля

Потрібно: **Docker Desktop** (з WSL 2) і **.NET SDK 10**.

**Автоматично (усі три БД однією командою):**

```bash
powershell -ExecutionPolicy Bypass -File scripts\deploy.ps1
```

Скрипт виконує кроки нижче і **запускає seed удруге**, щоб показати, що дублікати не з'являються.

**Вручну, по кроках:**

1. Запустити контейнери (SQL Server 2022 на `1433`, MongoDB 7 на `27017`):
   ```bash
   docker compose up -d --wait
   ```
2. **Проєкт №1.** Виконати по черзі `001_create_database` → `002_schema` → `003_procedures` → `004_seed` → `005_security` (замініть ім'я файлу):
   ```bash
   docker exec cinema-sql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "Cinema_Dev_2026!" -C -I -b -W -f 65001 -i /scripts/booking/001_create_database.sql
   ```
3. **Проєкт №2.** Застосувати міграції EF Core (створюють БД, таблиці й seed), потім виконати `catalog_security.sql` тією ж командою `sqlcmd` з шляхом `/scripts/catalog/catalog_security.sql`:
   ```bash
   dotnet tool restore
   ```
   ```bash
   dotnet ef database update --project src/Catalog/Catalog.Data
   ```
4. **Проєкт №3.** Виконати по черзі `01_collections` → `02_indexes` → `03_seed`:
   ```bash
   docker exec cinema-mongo mongosh --quiet /scripts/01_collections.js
   ```

Рядок підключення Каталогу за замовчуванням: `localhost,1433`, `sa`. Змінюється змінною середовища `CINEMA_CATALOG_DB`.

## Облікові записи

| Що | Логін | Пароль | Доступ |
|---|---|---|---|
| SQL Server, адміністратор | `sa` | `Cinema_Dev_2026!` | усе |
| Застосунок Бронювання | `booking_app` | `Kadr_Tickets#2026` | лише CinemaBooking: процедури, CRUD глядачів без DELETE |
| Застосунок Каталогу | `catalog_app` | `Kadr_Films#2026` | лише CinemaCatalog: читання/запис даних, без зміни схеми |
| MongoDB | — | — | без автентифікації (локальна розробка) |

Паролі навчальні. У реальному проєкті вони зберігаються в секретах, а не в репозиторії.

**SSMS:** сервер `localhost,1433`, «Проверка подлинности SQL Server», `sa` / пароль, ✅ «Доверенный сертификат сервера».
**MongoDB Compass:** `mongodb://localhost:27017`. Якщо на ПК встановлено ще й локальний MongoDB (служба `MongoDB`), він перехоплює `localhost`. Тоді використовуйте `mongodb://[::1]:27017` або зупиніть службу.

## Перевірка

| Що | Команда |
|---|---|
| Проєкт №1: CRUD, пагінація, обмеження, недопустимі переходи, конкурентність, FK, права | `docker exec cinema-sql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "Cinema_Dev_2026!" -C -I -W -f 65001 -i /scripts/booking/006_verify.sql` |
| Проєкт №1: повторний seed без дублікатів | той самий `sqlcmd` з `/scripts/booking/004_seed.sql` — таблиця кількостей не змінюється |
| Проєкт №2: зв'язки 1:N, 1:1, M:N | `dotnet run --project src/Catalog/Catalog.Data` |
| Проєкт №3: агрегації, embedding/reference, валідація | `docker exec cinema-mongo mongosh --quiet /scripts/04_queries.js` |
| Проєкт №3: повторний seed без дублікатів | `docker exec cinema-mongo mongosh --quiet /scripts/03_seed.js` → «додано 0» |

**Самоперевірка з умови:**
* `SELECT * FROM sys.foreign_keys` у кожній SQL-базі — усі FK ведуть на таблиці тієї ж бази (розділ 7 у `006_verify.sql`).
* «Використати» неоплачене бронювання → `50002 Недопустимий перехід статусу: Created -> Used` (розділ 5).
* `db.reviews.find()` повертає документи з різними наборами полів (`quick` / `full` / `critic`).
* Поля з таблиці дублювання не мають FK на іншу базу; знімки позначені коментарями в DDL.

Скинути все й почати з нуля:

```bash
docker compose down -v
```
