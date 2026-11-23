/* =====================================================================================================
   Лаб. 08 — синхронізація локальної копії довідника фільмів подіями з Каталогу + ідемпотентність споживача.
   Застосовує SchemaMigrator під час старту booking-api (історія — dbo.SchemaMigrations), один раз.
   Скрипт ідемпотентний: його можна виконати й вручну на вже оновленій БД.
   ===================================================================================================== */

/* Копія Movies: час останньої ЗАСТОСОВАНОЇ події Каталогу. Подія з меншим OccurredAt, що прийшла пізніше
   (out-of-order), не перезапише новіші дані. NULL — рядок із seed, ще не оновлювався подіями. */
IF COL_LENGTH(N'dbo.Movies', N'CatalogUpdatedAt') IS NULL
    ALTER TABLE dbo.Movies ADD CatalogUpdatedAt DATETIME2(7) NULL;
GO

/* Архівування фільму в Каталозі (м'яке видалення): нові сеанси на нього створювати не можна,
   а вже створені сеанси й бронювання лишаються (FK на Movies, тому рядок не видаляється). */
IF COL_LENGTH(N'dbo.Movies', N'IsArchived') IS NULL
    ALTER TABLE dbo.Movies ADD IsArchived BIT NOT NULL CONSTRAINT DF_Movies_IsArchived DEFAULT (0);
GO

/* Оброблені integration events: EventId — PRIMARY KEY (унікальність гарантує БД).
   Запис додається в ТІЙ САМІЙ транзакції, що й зміна копії Movies: або обидва, або нічого.
   Повторна доставка тієї самої події (at-least-once) бачить EventId і пропускається. */
IF OBJECT_ID(N'dbo.ProcessedEvents', N'U') IS NULL
CREATE TABLE dbo.ProcessedEvents (
    EventId     UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_ProcessedEvents PRIMARY KEY,
    EventType   VARCHAR(100)     NOT NULL,
    ProcessedAt DATETIME2(3)     NOT NULL CONSTRAINT DF_ProcessedEvents_ProcessedAt DEFAULT (SYSUTCDATETIME())
);
GO

/* Найменші привілеї (security.sql, Лаб. 01): копію Movies змінює лише синхронізація з Каталогом */
IF DATABASE_PRINCIPAL_ID(N'booking_service') IS NOT NULL
BEGIN
    GRANT INSERT, UPDATE ON dbo.Movies          TO booking_service;
    GRANT SELECT, INSERT ON dbo.ProcessedEvents TO booking_service;
END
GO
