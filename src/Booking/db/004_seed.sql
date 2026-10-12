/* =====================================================================
   Скрипт 004: тестові дані. ІДЕМПОТЕНТНИЙ — повторний запуск не створює дублікатів:
   кожен запис вставляється лише якщо його ще немає (INSERT ... WHERE NOT EXISTS / IF NOT EXISTS).
   Movies.Id збігаються з Movies.Id у CinemaCatalog (seed у CatalogSeed.cs).
   ===================================================================== */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
USE CinemaBooking;
GO
SET NOCOUNT ON;

/* ---------- Локальна копія фільмів (у робочому режимі приходить подіями з Каталогу) ---------- */
INSERT dbo.Movies (Id, Title, DurationMinutes, AgeRating)
SELECT v.Id, v.Title, v.Duration, v.Age
FROM (VALUES
    (1, N'Карпатський експрес',      118, '12+'),
    (2, N'Таємниця старого маяка',   104, '12+'),
    (3, N'Мандрівка до зірок',        92, '0+'),
    (4, N'Остання зміна',            131, '16+'),
    (5, N'Місто без світла',         109, '16+'),
    (6, N'Кіт на ім''я Борщ',          88, '6+')
) AS v (Id, Title, Duration, Age)
WHERE NOT EXISTS (SELECT 1 FROM dbo.Movies m WHERE m.Id = v.Id);

/* ---------- Зали ---------- */
SET IDENTITY_INSERT dbo.Halls ON;
INSERT dbo.Halls (Id, Name)
SELECT v.Id, v.Name
FROM (VALUES (1, N'Зал 1'), (2, N'Зал 2 «Комфорт»')) AS v (Id, Name)
WHERE NOT EXISTS (SELECT 1 FROM dbo.Halls h WHERE h.Id = v.Id);
SET IDENTITY_INSERT dbo.Halls OFF;

/* ---------- Місця: Зал 1 — 5 рядів × 8 (5-й ряд VIP), Зал 2 — 4 ряди × 6 (4-й ряд VIP) ---------- */
;WITH Numbers AS (SELECT n FROM (VALUES (1),(2),(3),(4),(5),(6),(7),(8)) AS t (n)),
Layout AS (
    SELECT 1 AS HallId, r.n AS RowNumber, s.n AS SeatNumber, IIF(r.n = 5, 'VIP', 'Standard') AS SeatType
    FROM Numbers r CROSS JOIN Numbers s WHERE r.n <= 5 AND s.n <= 8
    UNION ALL
    SELECT 2, r.n, s.n, IIF(r.n = 4, 'VIP', 'Standard')
    FROM Numbers r CROSS JOIN Numbers s WHERE r.n <= 4 AND s.n <= 6
)
INSERT dbo.Seats (HallId, RowNumber, SeatNumber, SeatType)
SELECT l.HallId, l.RowNumber, l.SeatNumber, l.SeatType
FROM Layout l
WHERE NOT EXISTS (SELECT 1 FROM dbo.Seats s
                  WHERE s.HallId = l.HallId AND s.RowNumber = l.RowNumber AND s.SeatNumber = l.SeatNumber);

/* ---------- Сеанси (Id фіксовані; час — відносно дня першого запуску) ---------- */
DECLARE @Tomorrow DATETIME2(0) = CAST(CAST(DATEADD(DAY, 1, SYSUTCDATETIME()) AS DATE) AS DATETIME2(0));
SET IDENTITY_INSERT dbo.Sessions ON;
INSERT dbo.Sessions (Id, MovieId, HallId, StartsAt, BasePrice, CreatedBy)
SELECT v.Id, v.MovieId, v.HallId, DATEADD(MINUTE, v.Minutes, @Tomorrow), v.Price, N'seed'
FROM (VALUES
    (1, 1, 1, 15 * 60, 150.00),   -- завтра 15:00 UTC, Зал 1
    (2, 4, 1, 19 * 60, 180.00),   -- завтра 19:00 UTC, Зал 1
    (3, 3, 2, 12 * 60, 120.00),   -- завтра 12:00 UTC, Зал 2
    (4, 2, 2, 18 * 60, 200.00),   -- завтра 18:00 UTC, Зал 2
    (5, 6, 1, 36 * 60, 130.00)    -- післязавтра 12:00 UTC, Зал 1
) AS v (Id, MovieId, HallId, Minutes, Price)
WHERE NOT EXISTS (SELECT 1 FROM dbo.Sessions s WHERE s.Id = v.Id);
SET IDENTITY_INSERT dbo.Sessions OFF;

/* ---------- Глядачі ---------- */
SET IDENTITY_INSERT dbo.Customers ON;
INSERT dbo.Customers (Id, FullName, Email, Phone, CreatedBy)
SELECT v.Id, v.FullName, v.Email, v.Phone, N'seed'
FROM (VALUES
    (1, N'Олена Коваль',   N'olena.koval@example.com',  '+380501234567'),
    (2, N'Тарас Мельник',  N'taras.melnyk@example.com', NULL),
    (3, N'Ірина Бондар',   N'iryna.bondar@example.com', '+380931234567')
) AS v (Id, FullName, Email, Phone)
WHERE NOT EXISTS (SELECT 1 FROM dbo.Customers c WHERE c.Id = v.Id);
SET IDENTITY_INSERT dbo.Customers OFF;
GO

/* ---------- Бронювання — через процедури, кожне лише якщо ще не існує ---------- */
DECLARE @Seats dbo.SeatIdList, @BookingId BIGINT, @Total DECIMAL(10,2);

-- №1: Олена, сеанс 1, ряд 3 місця 4–5, оплачено карткою  → Paid
IF NOT EXISTS (SELECT 1 FROM dbo.Bookings WHERE CustomerId = 1 AND SessionId = 1)
BEGIN
    INSERT @Seats SELECT Id FROM dbo.Seats WHERE HallId = 1 AND RowNumber = 3 AND SeatNumber IN (4, 5);
    EXEC dbo.usp_Booking_Create @CustomerId = 1, @SessionId = 1, @Seats = @Seats, @UserName = N'seed', @BookingId = @BookingId OUTPUT;
    SELECT @Total = TotalAmount FROM dbo.Bookings WHERE Id = @BookingId;
    EXEC dbo.usp_Booking_Pay @BookingId = @BookingId, @Amount = @Total, @Method = 'card', @TransactionId = 'SEED-TXN-0001', @UserName = N'seed';
END;

-- №2: Тарас, сеанс 1, VIP ряд 5 місце 1, ще не оплачено → Created
IF NOT EXISTS (SELECT 1 FROM dbo.Bookings WHERE CustomerId = 2 AND SessionId = 1)
BEGIN
    DELETE @Seats;
    INSERT @Seats SELECT Id FROM dbo.Seats WHERE HallId = 1 AND RowNumber = 5 AND SeatNumber = 1;
    EXEC dbo.usp_Booking_Create @CustomerId = 2, @SessionId = 1, @Seats = @Seats, @UserName = N'seed', @BookingId = @BookingId OUTPUT;
END;

-- №3: Ірина, сеанс 3 (мультфільм), 3 місця, оплачено і використано → Used
IF NOT EXISTS (SELECT 1 FROM dbo.Bookings WHERE CustomerId = 3 AND SessionId = 3)
BEGIN
    DELETE @Seats;
    INSERT @Seats SELECT Id FROM dbo.Seats WHERE HallId = 2 AND RowNumber = 2 AND SeatNumber IN (2, 3, 4);
    EXEC dbo.usp_Booking_Create @CustomerId = 3, @SessionId = 3, @Seats = @Seats, @UserName = N'seed', @BookingId = @BookingId OUTPUT;
    SELECT @Total = TotalAmount FROM dbo.Bookings WHERE Id = @BookingId;
    EXEC dbo.usp_Booking_Pay @BookingId = @BookingId, @Amount = @Total, @Method = 'online', @TransactionId = 'SEED-TXN-0002', @UserName = N'seed';
    EXEC dbo.usp_Booking_ChangeStatus @BookingId = @BookingId, @NewStatus = 'Used', @UserName = N'seed';
END;

-- №4: Олена, сеанс 2, скасовано → Cancelled (місце знову вільне)
IF NOT EXISTS (SELECT 1 FROM dbo.Bookings WHERE CustomerId = 1 AND SessionId = 2)
BEGIN
    DELETE @Seats;
    INSERT @Seats SELECT Id FROM dbo.Seats WHERE HallId = 1 AND RowNumber = 2 AND SeatNumber = 6;
    EXEC dbo.usp_Booking_Create @CustomerId = 1, @SessionId = 2, @Seats = @Seats, @UserName = N'seed', @BookingId = @BookingId OUTPUT;
    EXEC dbo.usp_Booking_ChangeStatus @BookingId = @BookingId, @NewStatus = 'Cancelled', @Reason = N'Змінились плани', @UserName = N'seed';
END;
GO

/* ---------- Контроль: кількість записів (при повторному запуску не змінюється) ---------- */
SELECT (SELECT COUNT(*) FROM dbo.Movies)    AS Movies,
       (SELECT COUNT(*) FROM dbo.Halls)     AS Halls,
       (SELECT COUNT(*) FROM dbo.Seats)     AS Seats,
       (SELECT COUNT(*) FROM dbo.Sessions)  AS Sessions,
       (SELECT COUNT(*) FROM dbo.Customers) AS Customers,
       (SELECT COUNT(*) FROM dbo.Bookings)  AS Bookings,
       (SELECT COUNT(*) FROM dbo.Tickets)   AS Tickets;
GO
