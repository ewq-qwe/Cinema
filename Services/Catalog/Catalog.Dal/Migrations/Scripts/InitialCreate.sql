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
    WHERE [MigrationId] = N'20261026153340_InitialCreate'
)
BEGIN
    CREATE TABLE [Genres] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(50) NOT NULL,
        CONSTRAINT [PK_Genres] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026153340_InitialCreate'
)
BEGIN
    CREATE TABLE [People] (
        [Id] int NOT NULL IDENTITY,
        [FullName] nvarchar(150) NOT NULL,
        [BirthYear] int NULL,
        [CreatedAt] datetime2(3) NOT NULL,
        CONSTRAINT [PK_People] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_People_BirthYear] CHECK ([BirthYear] IS NULL OR [BirthYear] >= 1850)
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026153340_InitialCreate'
)
BEGIN
    CREATE TABLE [Movies] (
        [Id] int NOT NULL IDENTITY,
        [Title] nvarchar(200) NOT NULL,
        [ReleaseYear] int NOT NULL,
        [DurationMinutes] int NOT NULL,
        [AgeRating] varchar(5) NOT NULL,
        [IsArchived] bit NOT NULL DEFAULT CAST(0 AS bit),
        [GenreId] int NOT NULL,
        [CreatedAt] datetime2(3) NOT NULL,
        [UpdatedAt] datetime2(3) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Movies] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Movies_AgeRating] CHECK ([AgeRating] IN ('0+', '6+', '12+', '16+', '18+')),
        CONSTRAINT [CK_Movies_Duration] CHECK ([DurationMinutes] BETWEEN 1 AND 600),
        CONSTRAINT [CK_Movies_ReleaseYear] CHECK ([ReleaseYear] >= 1895),
        CONSTRAINT [FK_Movies_Genres_GenreId] FOREIGN KEY ([GenreId]) REFERENCES [Genres] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026153340_InitialCreate'
)
BEGIN
    CREATE TABLE [MovieCredits] (
        [MovieId] int NOT NULL,
        [PersonId] int NOT NULL,
        [Role] varchar(10) NOT NULL,
        [CharacterName] nvarchar(100) NULL,
        [BillingOrder] int NOT NULL,
        [CreditedAt] datetime2(3) NOT NULL DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [PK_MovieCredits] PRIMARY KEY ([MovieId], [PersonId], [Role]),
        CONSTRAINT [CK_MovieCredits_BillingOrder] CHECK ([BillingOrder] >= 0),
        CONSTRAINT [CK_MovieCredits_Role] CHECK ([Role] IN ('Actor', 'Director')),
        CONSTRAINT [FK_MovieCredits_Movies_MovieId] FOREIGN KEY ([MovieId]) REFERENCES [Movies] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_MovieCredits_People_PersonId] FOREIGN KEY ([PersonId]) REFERENCES [People] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026153340_InitialCreate'
)
BEGIN
    CREATE TABLE [MovieDetails] (
        [MovieId] int NOT NULL,
        [Description] nvarchar(2000) NOT NULL,
        [Country] nvarchar(60) NOT NULL,
        [TrailerUrl] varchar(300) NULL,
        [PosterUrl] varchar(300) NULL,
        [Budget] decimal(18,2) NULL,
        CONSTRAINT [PK_MovieDetails] PRIMARY KEY ([MovieId]),
        CONSTRAINT [CK_MovieDetails_Budget] CHECK ([Budget] IS NULL OR [Budget] > 0),
        CONSTRAINT [FK_MovieDetails_Movies_MovieId] FOREIGN KEY ([MovieId]) REFERENCES [Movies] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026153340_InitialCreate'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Name') AND [object_id] = OBJECT_ID(N'[Genres]'))
        SET IDENTITY_INSERT [Genres] ON;
    EXEC(N'INSERT INTO [Genres] ([Id], [Name])
    VALUES (1, N''Пригоди''),
    (2, N''Детектив''),
    (3, N''Анімація''),
    (4, N''Драма''),
    (5, N''Трилер''),
    (6, N''Комедія'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Name') AND [object_id] = OBJECT_ID(N'[Genres]'))
        SET IDENTITY_INSERT [Genres] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026153340_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Genres_Name] ON [Genres] ([Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026153340_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_MovieCredits_PersonId_Role_BillingOrder] ON [MovieCredits] ([PersonId], [Role], [BillingOrder]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026153340_InitialCreate'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_Movies_GenreId_ReleaseYear] ON [Movies] ([GenreId], [ReleaseYear]) WHERE [IsArchived] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026153340_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Movies_Title] ON [Movies] ([Title]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026153340_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Movies_Title_ReleaseYear] ON [Movies] ([Title], [ReleaseYear]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026153340_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_People_FullName] ON [People] ([FullName]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026153340_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261026153340_InitialCreate', N'10.0.14');
END;

COMMIT;
GO

