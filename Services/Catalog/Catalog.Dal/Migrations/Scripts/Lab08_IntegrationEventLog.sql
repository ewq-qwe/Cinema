BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123172151_Lab08_IntegrationEventLog'
)
BEGIN
    CREATE TABLE [IntegrationEventLog] (
        [EventId] uniqueidentifier NOT NULL,
        [EventType] varchar(100) NOT NULL,
        [Exchange] varchar(100) NOT NULL,
        [RoutingKey] varchar(100) NOT NULL,
        [Content] nvarchar(max) NOT NULL,
        [OccurredAt] datetime2(7) NOT NULL,
        [CorrelationId] varchar(100) NULL,
        [SchemaVersion] int NOT NULL,
        [TraceParent] varchar(64) NULL,
        [State] int NOT NULL,
        [TimesSent] int NOT NULL,
        [PublishedAt] datetime2(7) NULL,
        [LastError] nvarchar(1000) NULL,
        CONSTRAINT [PK_IntegrationEventLog] PRIMARY KEY ([EventId]),
        CONSTRAINT [CK_IntegrationEventLog_State] CHECK ([State] IN (0, 1))
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123172151_Lab08_IntegrationEventLog'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_IntegrationEventLog_Pending] ON [IntegrationEventLog] ([State], [OccurredAt]) WHERE [State] = 0');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261123172151_Lab08_IntegrationEventLog'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261123172151_Lab08_IntegrationEventLog', N'10.0.14');
END;

COMMIT;
GO

