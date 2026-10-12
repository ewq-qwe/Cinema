/* =====================================================================
   Найменші привілеї для Каталогу: логін catalog_app з доступом ЛИШЕ до CinemaCatalog.
   Виконується після «dotnet ef database update». Пароль навчальний.
   ===================================================================== */
USE master;
GO
IF SUSER_ID(N'catalog_app') IS NULL
    CREATE LOGIN catalog_app WITH PASSWORD = N'Kadr_Films#2026', DEFAULT_DATABASE = CinemaCatalog, CHECK_POLICY = ON;
GO
USE CinemaCatalog;
GO
IF DATABASE_PRINCIPAL_ID(N'catalog_app') IS NULL
    CREATE USER catalog_app FOR LOGIN catalog_app;
-- Застосунок читає і змінює дані, але не змінює схему (схему змінюють лише міграції)
ALTER ROLE db_datareader ADD MEMBER catalog_app;
ALTER ROLE db_datawriter ADD MEMBER catalog_app;
GO
