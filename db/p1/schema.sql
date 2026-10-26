/* =====================================================================
   Проєкт №1. Мікросервіс «Бронювання квитків» кінотеатру «Кадр»
   schema.sql — створення БД CinemaBooking, таблиці, ключі, обмеження, індекси, представлення.
   Порядок розгортання: schema.sql → procedures.sql → seed.sql → security.sql (→ verify.sql)

   Навчальне середовище: БД створюється з нуля при кожному запуску schema.sql.
   (У production замість цього — інкрементальні скрипти змін.)

   Зв'язки всередині БД:
     1:1  Bookings ──── BookingPayments        (PK BookingPayments = FK на Bookings)
     1:N  Customers ─< Bookings, Halls ─< Seats, Halls ─< Sessions,
          Movies ─< Sessions, Sessions ─< Bookings
     M:N  Bookings >─ Tickets ─< Seats         (проміжна таблиця з власними полями: Price, IsCancelled)

   Посилання за межі БД — лише за ідентифікатором, БЕЗ FOREIGN KEY на іншу базу:
     Movies.Id = Movies.Id у CinemaCatalog (локальна копія довідника фільмів).
   ===================================================================== */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF DB_ID(N'CinemaBooking') IS NOT NULL
BEGIN
    ALTER DATABASE CinemaBooking SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE CinemaBooking;
END;
GO

CREATE DATABASE CinemaBooking COLLATE Ukrainian_100_CI_AS;
GO

USE CinemaBooking;
GO

/* ---------- Глядачі ---------- */
CREATE TABLE dbo.Customers (
    Id          BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Customers PRIMARY KEY,
    FullName    NVARCHAR(150) NOT NULL,
    Email       NVARCHAR(254) NOT NULL,
    Phone       VARCHAR(13)   NULL,
    CreatedAt   DATETIME2(3)  NOT NULL CONSTRAINT DF_Customers_CreatedAt DEFAULT (SYSUTCDATETIME()),
    CreatedBy   NVARCHAR(100) NULL,
    UpdatedAt   DATETIME2(3)  NULL,
    UpdatedBy   NVARCHAR(100) NULL,
    IsDeleted   BIT           NOT NULL CONSTRAINT DF_Customers_IsDeleted DEFAULT (0),
    RowVersion  ROWVERSION    NOT NULL,
    CONSTRAINT CK_Customers_Email CHECK (Email LIKE '%_@_%._%'),
    CONSTRAINT CK_Customers_Phone CHECK (Phone IS NULL OR Phone LIKE '+380[0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9]')
);
-- Запит: вхід / пошук глядача за email. Унікальність лише серед не видалених
CREATE UNIQUE INDEX UX_Customers_Email ON dbo.Customers (Email) WHERE IsDeleted = 0;

/* ---------- Локальна копія довідника фільмів (власник — CinemaCatalog) ---------- */
CREATE TABLE dbo.Movies (
    Id              BIGINT        NOT NULL CONSTRAINT PK_Movies PRIMARY KEY,  -- той самий Id, що в CinemaCatalog; без IDENTITY
    Title           NVARCHAR(200) NOT NULL,
    DurationMinutes INT           NOT NULL,
    AgeRating       VARCHAR(5)    NOT NULL,
    SyncedAt        DATETIME2(3)  NOT NULL CONSTRAINT DF_Movies_SyncedAt DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT CK_Movies_Duration  CHECK (DurationMinutes BETWEEN 1 AND 600),
    CONSTRAINT CK_Movies_AgeRating CHECK (AgeRating IN ('0+', '6+', '12+', '16+', '18+'))
);

/* ---------- Зали та місця ---------- */
CREATE TABLE dbo.Halls (
    Id        INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Halls PRIMARY KEY,
    Name      NVARCHAR(50) NOT NULL CONSTRAINT UQ_Halls_Name UNIQUE,
    IsActive  BIT          NOT NULL CONSTRAINT DF_Halls_IsActive DEFAULT (1)
);

CREATE TABLE dbo.Seats (
    Id          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Seats PRIMARY KEY,
    HallId      INT         NOT NULL CONSTRAINT FK_Seats_Halls REFERENCES dbo.Halls (Id),
    RowNumber   TINYINT     NOT NULL,
    SeatNumber  TINYINT     NOT NULL,
    SeatType    VARCHAR(10) NOT NULL CONSTRAINT DF_Seats_SeatType DEFAULT ('Standard'),
    CONSTRAINT UQ_Seats_HallRowSeat UNIQUE (HallId, RowNumber, SeatNumber),   -- одне фізичне місце = один рядок
    CONSTRAINT CK_Seats_Type    CHECK (SeatType IN ('Standard', 'VIP')),
    CONSTRAINT CK_Seats_Numbers CHECK (RowNumber BETWEEN 1 AND 30 AND SeatNumber BETWEEN 1 AND 40)
);

/* ---------- Сеанси ---------- */
CREATE TABLE dbo.Sessions (
    Id         BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Sessions PRIMARY KEY,
    MovieId    BIGINT        NOT NULL CONSTRAINT FK_Sessions_Movies REFERENCES dbo.Movies (Id),  -- FK на ЛОКАЛЬНУ копію
    HallId     INT           NOT NULL CONSTRAINT FK_Sessions_Halls  REFERENCES dbo.Halls (Id),
    StartsAt   DATETIME2(0)  NOT NULL,
    BasePrice  DECIMAL(10,2) NOT NULL,
    CreatedAt  DATETIME2(3)  NOT NULL CONSTRAINT DF_Sessions_CreatedAt DEFAULT (SYSUTCDATETIME()),
    CreatedBy  NVARCHAR(100) NULL,
    UpdatedAt  DATETIME2(3)  NULL,
    UpdatedBy  NVARCHAR(100) NULL,
    CONSTRAINT UQ_Sessions_HallStart UNIQUE (HallId, StartsAt),               -- в одному залі два сеанси одночасно не почнуться
    CONSTRAINT CK_Sessions_Price CHECK (BasePrice > 0)
);
-- Запит: розклад — «сеанси фільму, що починаються після дати X»
CREATE INDEX IX_Sessions_Movie_StartsAt ON dbo.Sessions (MovieId, StartsAt) INCLUDE (HallId, BasePrice);

/* ---------- Бронювання (корінь агрегату) ---------- */
CREATE TABLE dbo.Bookings (
    Id               BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Bookings PRIMARY KEY,
    CustomerId       BIGINT        NOT NULL,
    SessionId        BIGINT        NOT NULL,
    Status           VARCHAR(20)   NOT NULL CONSTRAINT DF_Bookings_Status DEFAULT ('Created'),
    MovieTitle       NVARCHAR(200) NOT NULL,   -- ЗНІМОК з Movies на момент бронювання
    SessionStartsAt  DATETIME2(0)  NOT NULL,   -- ЗНІМОК часу сеансу (для квитка та листа клієнту)
    TotalAmount      DECIMAL(10,2) NOT NULL,
    CancelReason     NVARCHAR(300) NULL,
    CreatedAt        DATETIME2(3)  NOT NULL CONSTRAINT DF_Bookings_CreatedAt DEFAULT (SYSUTCDATETIME()),
    CreatedBy        NVARCHAR(100) NULL,
    UpdatedAt        DATETIME2(3)  NULL,
    UpdatedBy        NVARCHAR(100) NULL,
    IsDeleted        BIT           NOT NULL CONSTRAINT DF_Bookings_IsDeleted DEFAULT (0),
    RowVersion       ROWVERSION    NOT NULL,
    -- Без каскадів: історію бронювань глядача не втрачаємо
    CONSTRAINT FK_Bookings_Customers FOREIGN KEY (CustomerId) REFERENCES dbo.Customers (Id),
    CONSTRAINT FK_Bookings_Sessions  FOREIGN KEY (SessionId)  REFERENCES dbo.Sessions (Id),
    CONSTRAINT CK_Bookings_Status CHECK (Status IN ('Created', 'Paid', 'Used', 'Cancelled')),
    CONSTRAINT CK_Bookings_Total  CHECK (TotalAmount >= 0)
);
-- Запит: «мої бронювання» — бронювання глядача, нові спочатку (покриваючий, лише «живі» записи)
CREATE INDEX IX_Bookings_Customer ON dbo.Bookings (CustomerId, CreatedAt DESC)
    INCLUDE (Status, MovieTitle, SessionStartsAt, TotalAmount) WHERE IsDeleted = 0;
-- Запит: касир шукає неоплачені бронювання сеансу (фільтрований — лише Created)
CREATE INDEX IX_Bookings_Session_Unpaid ON dbo.Bookings (SessionId) WHERE Status = 'Created';

/* ---------- 1:1 Оплата бронювання ---------- */
CREATE TABLE dbo.BookingPayments (
    BookingId      BIGINT        NOT NULL CONSTRAINT PK_BookingPayments PRIMARY KEY,
    Amount         DECIMAL(10,2) NOT NULL,
    Method         VARCHAR(10)   NOT NULL,
    TransactionId  VARCHAR(64)   NOT NULL,
    PaidAt         DATETIME2(3)  NOT NULL CONSTRAINT DF_BookingPayments_PaidAt DEFAULT (SYSUTCDATETIME()),
    -- Оплата — частина агрегату бронювання: каскад доречний
    CONSTRAINT FK_BookingPayments_Bookings FOREIGN KEY (BookingId) REFERENCES dbo.Bookings (Id) ON DELETE CASCADE,
    CONSTRAINT UQ_BookingPayments_Txn UNIQUE (TransactionId),                 -- той самий платіж не зарахується двічі
    CONSTRAINT CK_BookingPayments_Amount CHECK (Amount > 0),
    CONSTRAINT CK_BookingPayments_Method CHECK (Method IN ('card', 'cash', 'online'))
);

/* ---------- M:N Квитки: Bookings <-> Seats ---------- */
CREATE TABLE dbo.Tickets (
    BookingId    BIGINT        NOT NULL,
    SeatId       INT           NOT NULL,
    SessionId    BIGINT        NOT NULL,   -- дублюється з Bookings, щоб унікальний індекс міг заборонити продаж місця двічі
    Price        DECIMAL(10,2) NOT NULL,   -- ЗНІМОК ціни на момент бронювання
    IsCancelled  BIT           NOT NULL CONSTRAINT DF_Tickets_IsCancelled DEFAULT (0),
    CONSTRAINT PK_Tickets PRIMARY KEY (BookingId, SeatId),
    CONSTRAINT FK_Tickets_Bookings FOREIGN KEY (BookingId) REFERENCES dbo.Bookings (Id) ON DELETE CASCADE,
    CONSTRAINT FK_Tickets_Seats    FOREIGN KEY (SeatId)    REFERENCES dbo.Seats (Id),
    CONSTRAINT FK_Tickets_Sessions FOREIGN KEY (SessionId) REFERENCES dbo.Sessions (Id),
    CONSTRAINT CK_Tickets_Price CHECK (Price > 0)
);
-- Запит/правило: місце на сеансі може мати лише один дійсний квиток (скасовані не враховуються)
CREATE UNIQUE INDEX UX_Tickets_SessionSeat_Active ON dbo.Tickets (SessionId, SeatId) WHERE IsCancelled = 0;
GO

/* ---------- Денормалізоване представлення для читання: «квитки глядача» ---------- */
CREATE OR ALTER VIEW dbo.vw_CustomerTickets
AS
SELECT  b.Id            AS BookingId,
        c.FullName      AS CustomerName,
        c.Email,
        b.MovieTitle,
        b.SessionStartsAt,
        h.Name          AS HallName,
        s.RowNumber,
        s.SeatNumber,
        s.SeatType,
        t.Price,
        b.Status
FROM    dbo.Tickets t
JOIN    dbo.Bookings b  ON b.Id = t.BookingId
JOIN    dbo.Customers c ON c.Id = b.CustomerId
JOIN    dbo.Seats s     ON s.Id = t.SeatId
JOIN    dbo.Halls h     ON h.Id = s.HallId
WHERE   t.IsCancelled = 0 AND b.IsDeleted = 0;
GO

/* ---------- Табличний тип: список місць для процедури бронювання ---------- */
CREATE TYPE dbo.SeatIdList AS TABLE (SeatId INT NOT NULL PRIMARY KEY);
GO
