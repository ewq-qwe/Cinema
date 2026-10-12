IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012155418_InitialCreate'
)
BEGIN
    CREATE TABLE [Genres] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(50) NOT NULL,
        [CreatedAt] datetime2(3) NOT NULL DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] nvarchar(100) NULL,
        [UpdatedAt] datetime2(3) NULL,
        [UpdatedBy] nvarchar(100) NULL,
        CONSTRAINT [PK_Genres] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012155418_InitialCreate'
)
BEGIN
    CREATE TABLE [People] (
        [Id] int NOT NULL IDENTITY,
        [FullName] nvarchar(150) NOT NULL,
        [BirthYear] int NULL,
        [CreatedAt] datetime2(3) NOT NULL DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] nvarchar(100) NULL,
        [UpdatedAt] datetime2(3) NULL,
        [UpdatedBy] nvarchar(100) NULL,
        CONSTRAINT [PK_People] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_People_BirthYear] CHECK ([BirthYear] IS NULL OR [BirthYear] BETWEEN 1850 AND 2025)
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012155418_InitialCreate'
)
BEGIN
    CREATE TABLE [Movies] (
        [Id] bigint NOT NULL IDENTITY,
        [GenreId] int NOT NULL,
        [Title] nvarchar(200) NOT NULL,
        [ReleaseYear] int NOT NULL,
        [DurationMinutes] int NOT NULL,
        [AgeRating] varchar(5) NOT NULL DEFAULT '0+',
        [IsDeleted] bit NOT NULL,
        [RowVersion] rowversion NOT NULL,
        [CreatedAt] datetime2(3) NOT NULL DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] nvarchar(100) NULL,
        [UpdatedAt] datetime2(3) NULL,
        [UpdatedBy] nvarchar(100) NULL,
        CONSTRAINT [PK_Movies] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Movies_AgeRating] CHECK ([AgeRating] IN ('0+', '6+', '12+', '16+', '18+')),
        CONSTRAINT [CK_Movies_Duration] CHECK ([DurationMinutes] BETWEEN 1 AND 600),
        CONSTRAINT [CK_Movies_ReleaseYear] CHECK ([ReleaseYear] BETWEEN 1895 AND 2100),
        CONSTRAINT [FK_Movies_Genres_GenreId] FOREIGN KEY ([GenreId]) REFERENCES [Genres] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012155418_InitialCreate'
)
BEGIN
    CREATE TABLE [MovieCredits] (
        [MovieId] bigint NOT NULL,
        [PersonId] int NOT NULL,
        [Role] varchar(10) NOT NULL,
        [CharacterName] nvarchar(100) NULL,
        [BillingOrder] int NOT NULL,
        CONSTRAINT [PK_MovieCredits] PRIMARY KEY ([MovieId], [PersonId], [Role]),
        CONSTRAINT [CK_MovieCredits_Order] CHECK ([BillingOrder] >= 0),
        CONSTRAINT [CK_MovieCredits_Role] CHECK ([Role] IN ('Actor', 'Director')),
        CONSTRAINT [FK_MovieCredits_Movies_MovieId] FOREIGN KEY ([MovieId]) REFERENCES [Movies] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_MovieCredits_People_PersonId] FOREIGN KEY ([PersonId]) REFERENCES [People] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012155418_InitialCreate'
)
BEGIN
    CREATE TABLE [MovieDetails] (
        [MovieId] bigint NOT NULL,
        [Description] nvarchar(2000) NOT NULL,
        [Country] nvarchar(60) NOT NULL,
        [TrailerUrl] varchar(300) NULL,
        [PosterUrl] varchar(300) NULL,
        CONSTRAINT [PK_MovieDetails] PRIMARY KEY ([MovieId]),
        CONSTRAINT [FK_MovieDetails_Movies_MovieId] FOREIGN KEY ([MovieId]) REFERENCES [Movies] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012155418_InitialCreate'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'CreatedAt', N'CreatedBy', N'Name', N'UpdatedAt', N'UpdatedBy') AND [object_id] = OBJECT_ID(N'[Genres]'))
        SET IDENTITY_INSERT [Genres] ON;
    EXEC(N'INSERT INTO [Genres] ([Id], [CreatedAt], [CreatedBy], [Name], [UpdatedAt], [UpdatedBy])
    VALUES (1, ''2026-09-01T00:00:00.000Z'', N''seed'', N''Пригоди'', NULL, NULL),
    (2, ''2026-09-01T00:00:00.000Z'', N''seed'', N''Детектив'', NULL, NULL),
    (3, ''2026-09-01T00:00:00.000Z'', N''seed'', N''Анімація'', NULL, NULL),
    (4, ''2026-09-01T00:00:00.000Z'', N''seed'', N''Драма'', NULL, NULL),
    (5, ''2026-09-01T00:00:00.000Z'', N''seed'', N''Трилер'', NULL, NULL),
    (6, ''2026-09-01T00:00:00.000Z'', N''seed'', N''Комедія'', NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'CreatedAt', N'CreatedBy', N'Name', N'UpdatedAt', N'UpdatedBy') AND [object_id] = OBJECT_ID(N'[Genres]'))
        SET IDENTITY_INSERT [Genres] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012155418_InitialCreate'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BirthYear', N'CreatedAt', N'CreatedBy', N'FullName', N'UpdatedAt', N'UpdatedBy') AND [object_id] = OBJECT_ID(N'[People]'))
        SET IDENTITY_INSERT [People] ON;
    EXEC(N'INSERT INTO [People] ([Id], [BirthYear], [CreatedAt], [CreatedBy], [FullName], [UpdatedAt], [UpdatedBy])
    VALUES (1, 1978, ''2026-09-01T00:00:00.000Z'', N''seed'', N''Андрій Гнатюк'', NULL, NULL),
    (2, 1995, ''2026-09-01T00:00:00.000Z'', N''seed'', N''Марта Савчук'', NULL, NULL),
    (3, 1988, ''2026-09-01T00:00:00.000Z'', N''seed'', N''Олег Кравець'', NULL, NULL),
    (4, 2001, ''2026-09-01T00:00:00.000Z'', N''seed'', N''Софія Лисенко'', NULL, NULL),
    (5, 1970, ''2026-09-01T00:00:00.000Z'', N''seed'', N''Богдан Левицький'', NULL, NULL),
    (6, 1992, ''2026-09-01T00:00:00.000Z'', N''seed'', N''Ярина Пасічник'', NULL, NULL),
    (7, 1983, ''2026-09-01T00:00:00.000Z'', N''seed'', N''Дмитро Онищенко'', NULL, NULL),
    (8, 1990, ''2026-09-01T00:00:00.000Z'', N''seed'', N''Наталія Руденко'', NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'BirthYear', N'CreatedAt', N'CreatedBy', N'FullName', N'UpdatedAt', N'UpdatedBy') AND [object_id] = OBJECT_ID(N'[People]'))
        SET IDENTITY_INSERT [People] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012155418_InitialCreate'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AgeRating', N'CreatedAt', N'CreatedBy', N'DurationMinutes', N'GenreId', N'IsDeleted', N'ReleaseYear', N'Title', N'UpdatedAt', N'UpdatedBy') AND [object_id] = OBJECT_ID(N'[Movies]'))
        SET IDENTITY_INSERT [Movies] ON;
    EXEC(N'INSERT INTO [Movies] ([Id], [AgeRating], [CreatedAt], [CreatedBy], [DurationMinutes], [GenreId], [IsDeleted], [ReleaseYear], [Title], [UpdatedAt], [UpdatedBy])
    VALUES (CAST(1 AS bigint), ''12+'', ''2026-09-01T00:00:00.000Z'', N''seed'', 118, 1, CAST(0 AS bit), 2025, N''Карпатський експрес'', NULL, NULL),
    (CAST(2 AS bigint), ''12+'', ''2026-09-01T00:00:00.000Z'', N''seed'', 104, 2, CAST(0 AS bit), 2024, N''Таємниця старого маяка'', NULL, NULL),
    (CAST(3 AS bigint), ''0+'', ''2026-09-01T00:00:00.000Z'', N''seed'', 92, 3, CAST(0 AS bit), 2025, N''Мандрівка до зірок'', NULL, NULL),
    (CAST(4 AS bigint), ''16+'', ''2026-09-01T00:00:00.000Z'', N''seed'', 131, 4, CAST(0 AS bit), 2023, N''Остання зміна'', NULL, NULL),
    (CAST(5 AS bigint), ''16+'', ''2026-09-01T00:00:00.000Z'', N''seed'', 109, 5, CAST(0 AS bit), 2024, N''Місто без світла'', NULL, NULL),
    (CAST(6 AS bigint), ''6+'', ''2026-09-01T00:00:00.000Z'', N''seed'', 88, 6, CAST(0 AS bit), 2025, N''Кіт на ім''''я Борщ'', NULL, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'AgeRating', N'CreatedAt', N'CreatedBy', N'DurationMinutes', N'GenreId', N'IsDeleted', N'ReleaseYear', N'Title', N'UpdatedAt', N'UpdatedBy') AND [object_id] = OBJECT_ID(N'[Movies]'))
        SET IDENTITY_INSERT [Movies] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012155418_InitialCreate'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'MovieId', N'PersonId', N'Role', N'BillingOrder', N'CharacterName') AND [object_id] = OBJECT_ID(N'[MovieCredits]'))
        SET IDENTITY_INSERT [MovieCredits] ON;
    EXEC(N'INSERT INTO [MovieCredits] ([MovieId], [PersonId], [Role], [BillingOrder], [CharacterName])
    VALUES (CAST(1 AS bigint), 1, ''Director'', 0, NULL),
    (CAST(1 AS bigint), 2, ''Actor'', 2, N''Зоряна''),
    (CAST(1 AS bigint), 3, ''Actor'', 1, N''Мирон''),
    (CAST(2 AS bigint), 3, ''Actor'', 2, N''доглядач маяка''),
    (CAST(2 AS bigint), 5, ''Director'', 0, NULL),
    (CAST(2 AS bigint), 6, ''Actor'', 1, N''слідча Ганна Ровенко''),
    (CAST(3 AS bigint), 4, ''Actor'', 1, N''Тимко (голос)''),
    (CAST(3 AS bigint), 7, ''Director'', 0, NULL),
    (CAST(4 AS bigint), 2, ''Actor'', 2, N''медсестра Оля''),
    (CAST(4 AS bigint), 5, ''Director'', 0, NULL),
    (CAST(4 AS bigint), 8, ''Actor'', 1, N''лікарка Віра''),
    (CAST(5 AS bigint), 1, ''Director'', 0, NULL),
    (CAST(5 AS bigint), 3, ''Actor'', 1, N''детектив Шевчук''),
    (CAST(5 AS bigint), 6, ''Actor'', 2, N''журналістка''),
    (CAST(6 AS bigint), 4, ''Actor'', 1, N''ведуча шоу''),
    (CAST(6 AS bigint), 7, ''Director'', 0, NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'MovieId', N'PersonId', N'Role', N'BillingOrder', N'CharacterName') AND [object_id] = OBJECT_ID(N'[MovieCredits]'))
        SET IDENTITY_INSERT [MovieCredits] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012155418_InitialCreate'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'MovieId', N'Country', N'Description', N'PosterUrl', N'TrailerUrl') AND [object_id] = OBJECT_ID(N'[MovieDetails]'))
        SET IDENTITY_INSERT [MovieDetails] ON;
    EXEC(N'INSERT INTO [MovieDetails] ([MovieId], [Country], [Description], [PosterUrl], [TrailerUrl])
    VALUES (CAST(1 AS bigint), N''Україна'', N''Потяг через гори, зниклий вагон із золотом і троє незнайомців, яким доведеться довіритися одне одному.'', ''https://cdn.cinema-kadr.example/posters/1.jpg'', NULL),
    (CAST(2 AS bigint), N''Україна'', N''Молода слідча приїжджає в приморське містечко, де щоночі на покинутому маяку загоряється світло.'', ''https://cdn.cinema-kadr.example/posters/2.jpg'', NULL),
    (CAST(3 AS bigint), N''Україна'', N''Хлопчик і його робот-пес будують ракету з картонних коробок — і вона справді злітає.'', ''https://cdn.cinema-kadr.example/posters/3.jpg'', NULL),
    (CAST(4 AS bigint), N''Україна, Польща'', N''Одна нічна зміна в лікарні маленького міста, що змінює життя п''''ятьох людей.'', ''https://cdn.cinema-kadr.example/posters/4.jpg'', NULL),
    (CAST(5 AS bigint), N''Україна'', N''Місто занурюється в темряву, і хтось використовує це, щоб приховати злочин.'', ''https://cdn.cinema-kadr.example/posters/5.jpg'', NULL),
    (CAST(6 AS bigint), N''Україна'', N''Рудий кіт випадково стає зіркою кулінарного шоу.'', ''https://cdn.cinema-kadr.example/posters/6.jpg'', NULL)');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'MovieId', N'Country', N'Description', N'PosterUrl', N'TrailerUrl') AND [object_id] = OBJECT_ID(N'[MovieDetails]'))
        SET IDENTITY_INSERT [MovieDetails] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012155418_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Genres_Name] ON [Genres] ([Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012155418_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_MovieCredits_PersonId] ON [MovieCredits] ([PersonId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012155418_InitialCreate'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_Movies_GenreId_ReleaseYear] ON [Movies] ([GenreId], [ReleaseYear]) WHERE [IsDeleted] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012155418_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Movies_Title] ON [Movies] ([Title]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012155418_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Movies_Title_ReleaseYear] ON [Movies] ([Title], [ReleaseYear]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012155418_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_People_FullName] ON [People] ([FullName]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261012155418_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261012155418_InitialCreate', N'10.0.12');
END;

COMMIT;
GO

