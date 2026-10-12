/* =====================================================================
   Проєкт №2. Мікросервіс «Каталог фільмів» кінотеатру «Кадр»
   schema.sql — БД CinemaCatalog: 5 сутностей, ключі, обмеження, індекси.
   Порядок розгортання: schema.sql → seed.sql → security.sql

   Схема збігається з моделлю EF Core Code First (src/Catalog/Catalog.Data) і міграціями
   InitialCreate + AddMovieIsDeletedDefault. Рядки в __EFMigrationsHistory позначають їх як
   застосовані, тому «dotnet ef database update» поверх цієї БД нічого не змінює, а нові
   міграції (Лаб. 03) застосовуються далі звичайним шляхом.

   Зв'язки всередині БД:
     1:N  Genres ─< Movies                      (FK на боці «багатьох», ON DELETE NO ACTION)
     1:1  Movies ──── MovieDetails              (PK MovieDetails = FK на Movies)
     M:N  Movies >─ MovieCredits ─< People      (проміжна сутність з власними полями:
                                                 Role, CharacterName, BillingOrder)
   Movies.Id — джерело (власник) даних про фільми для CinemaBooking і cinema_reviews.
   ===================================================================== */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

-- Навчальне середовище: БД створюється з нуля при кожному запуску
IF DB_ID(N'CinemaCatalog') IS NOT NULL
BEGIN
    ALTER DATABASE CinemaCatalog SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE CinemaCatalog;
END;
GO
CREATE DATABASE CinemaCatalog COLLATE Ukrainian_100_CI_AS;
GO
USE CinemaCatalog;
GO

/* ---------- Жанри ---------- */
CREATE TABLE dbo.Genres (
    Id         INT IDENTITY(1,1) NOT NULL,
    Name       NVARCHAR(50)  NOT NULL,
    CreatedAt  DATETIME2(3)  NOT NULL DEFAULT (SYSUTCDATETIME()),
    CreatedBy  NVARCHAR(100) NULL,
    UpdatedAt  DATETIME2(3)  NULL,
    UpdatedBy  NVARCHAR(100) NULL,
    CONSTRAINT PK_Genres PRIMARY KEY (Id)
);

/* ---------- Люди (актори, режисери) ---------- */
CREATE TABLE dbo.People (
    Id         INT IDENTITY(1,1) NOT NULL,
    FullName   NVARCHAR(150) NOT NULL,
    BirthYear  INT           NULL,
    CreatedAt  DATETIME2(3)  NOT NULL DEFAULT (SYSUTCDATETIME()),
    CreatedBy  NVARCHAR(100) NULL,
    UpdatedAt  DATETIME2(3)  NULL,
    UpdatedBy  NVARCHAR(100) NULL,
    CONSTRAINT PK_People PRIMARY KEY (Id),
    CONSTRAINT CK_People_BirthYear CHECK (BirthYear IS NULL OR BirthYear BETWEEN 1850 AND 2025)
);

/* ---------- Фільми (корінь агрегату) ---------- */
CREATE TABLE dbo.Movies (
    Id               BIGINT IDENTITY(1,1) NOT NULL,
    GenreId          INT           NOT NULL,
    Title            NVARCHAR(200) NOT NULL,
    ReleaseYear      INT           NOT NULL,
    DurationMinutes  INT           NOT NULL,
    AgeRating        VARCHAR(5)    NOT NULL DEFAULT ('0+'),
    IsDeleted        BIT           NOT NULL DEFAULT (CAST(0 AS BIT)),      -- soft delete
    RowVersion       ROWVERSION    NOT NULL,                               -- оптимістична конкурентність
    CreatedAt        DATETIME2(3)  NOT NULL DEFAULT (SYSUTCDATETIME()),
    CreatedBy        NVARCHAR(100) NULL,
    UpdatedAt        DATETIME2(3)  NULL,
    UpdatedBy        NVARCHAR(100) NULL,
    CONSTRAINT PK_Movies PRIMARY KEY (Id),
    CONSTRAINT CK_Movies_AgeRating   CHECK (AgeRating IN ('0+', '6+', '12+', '16+', '18+')),
    CONSTRAINT CK_Movies_Duration    CHECK (DurationMinutes BETWEEN 1 AND 600),
    CONSTRAINT CK_Movies_ReleaseYear CHECK (ReleaseYear BETWEEN 1895 AND 2100),
    -- 1:N; без каскаду — жанр, до якого належать фільми, видалити не можна
    CONSTRAINT FK_Movies_Genres_GenreId FOREIGN KEY (GenreId) REFERENCES dbo.Genres (Id) ON DELETE NO ACTION
);

/* ---------- M:N Movies <-> People з власними полями ---------- */
CREATE TABLE dbo.MovieCredits (
    MovieId        BIGINT        NOT NULL,
    PersonId       INT           NOT NULL,
    Role           VARCHAR(10)   NOT NULL,      -- власне поле: Actor | Director
    CharacterName  NVARCHAR(100) NULL,          -- власне поле: ім'я персонажа
    BillingOrder   INT           NOT NULL,      -- власне поле: порядок у титрах
    CONSTRAINT PK_MovieCredits PRIMARY KEY (MovieId, PersonId, Role),
    CONSTRAINT CK_MovieCredits_Order CHECK (BillingOrder >= 0),
    CONSTRAINT CK_MovieCredits_Role  CHECK (Role IN ('Actor', 'Director')),
    -- команда — частина агрегату фільму: каскад доречний; людину з фільмографією видалити не можна
    CONSTRAINT FK_MovieCredits_Movies_MovieId  FOREIGN KEY (MovieId)  REFERENCES dbo.Movies (Id) ON DELETE CASCADE,
    CONSTRAINT FK_MovieCredits_People_PersonId FOREIGN KEY (PersonId) REFERENCES dbo.People (Id) ON DELETE NO ACTION
);

/* ---------- 1:1 Розширений опис фільму ---------- */
CREATE TABLE dbo.MovieDetails (
    MovieId      BIGINT         NOT NULL,     -- PK і водночас FK
    Description  NVARCHAR(2000) NOT NULL,
    Country      NVARCHAR(60)   NOT NULL,
    TrailerUrl   VARCHAR(300)   NULL,
    PosterUrl    VARCHAR(300)   NULL,
    CONSTRAINT PK_MovieDetails PRIMARY KEY (MovieId),
    CONSTRAINT FK_MovieDetails_Movies_MovieId FOREIGN KEY (MovieId) REFERENCES dbo.Movies (Id) ON DELETE CASCADE
);
GO

/* ---------- Індекси — кожен під конкретний запит ---------- */
-- Правило + запит: жанр з такою назвою лише один; вибір жанру за назвою
CREATE UNIQUE INDEX IX_Genres_Name ON dbo.Genres (Name);
-- Запит: пошук фільму за назвою (WHERE Title LIKE N'Карп%')
CREATE INDEX IX_Movies_Title ON dbo.Movies (Title);
-- Правило: фільм однозначно визначається назвою і роком випуску
CREATE UNIQUE INDEX IX_Movies_Title_ReleaseYear ON dbo.Movies (Title, ReleaseYear);
-- Запит: афіша жанру, новіші спочатку — лише не видалені (фільтрований)
CREATE INDEX IX_Movies_GenreId_ReleaseYear ON dbo.Movies (GenreId, ReleaseYear) WHERE IsDeleted = 0;
-- Запит: фільмографія людини (JOIN MovieCredits за PersonId)
CREATE INDEX IX_MovieCredits_PersonId ON dbo.MovieCredits (PersonId);
-- Запит: пошук актора/режисера за іменем
CREATE INDEX IX_People_FullName ON dbo.People (FullName);
GO

/* ---------- Облік міграцій EF Core (схема вже відповідає цим міграціям) ---------- */
CREATE TABLE dbo.__EFMigrationsHistory (
    MigrationId     NVARCHAR(150) NOT NULL,
    ProductVersion  NVARCHAR(32)  NOT NULL,
    CONSTRAINT PK___EFMigrationsHistory PRIMARY KEY (MigrationId)
);
INSERT dbo.__EFMigrationsHistory (MigrationId, ProductVersion) VALUES
    (N'20261012155418_InitialCreate',            N'10.0.12'),
    (N'20261012172423_AddMovieIsDeletedDefault', N'10.0.12');
GO
