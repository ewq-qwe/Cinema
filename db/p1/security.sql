/* =====================================================================
   security.sql — найменші привілеї — окремий логін застосунку з доступом ЛИШЕ до CinemaBooking.
   Пароль навчальний; у реальному проєкті — із секретів (user-secrets / Key Vault).
   ===================================================================== */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
USE master;
GO
IF SUSER_ID(N'booking_app') IS NULL
    CREATE LOGIN booking_app WITH PASSWORD = N'Kadr_Tickets#2026', DEFAULT_DATABASE = CinemaBooking, CHECK_POLICY = ON;
GO

USE CinemaBooking;
GO
IF DATABASE_PRINCIPAL_ID(N'booking_app') IS NULL
    CREATE USER booking_app FOR LOGIN booking_app;

IF DATABASE_PRINCIPAL_ID(N'booking_service') IS NULL
    CREATE ROLE booking_service;

-- Бізнес-операції — через процедури (і тип-параметр SeatIdList)
GRANT EXECUTE ON SCHEMA::dbo TO booking_service;
-- Прямий доступ лише до тих таблиць і операцій, які потрібні DAL-у (Лаб. 02, Services/Booking)
GRANT SELECT, INSERT, UPDATE         ON dbo.Customers       TO booking_service;
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.Sessions        TO booking_service;   -- розклад (CRUD)
GRANT SELECT, INSERT, UPDATE         ON dbo.Bookings        TO booking_service;
GRANT SELECT, INSERT, UPDATE         ON dbo.Tickets         TO booking_service;
GRANT SELECT, INSERT                 ON dbo.BookingPayments TO booking_service;
GRANT SELECT ON dbo.Movies   TO booking_service;   -- копія довідника: змінює лише синхронізація з Каталогом
GRANT SELECT ON dbo.Halls    TO booking_service;
GRANT SELECT ON dbo.Seats    TO booking_service;
GRANT SELECT ON dbo.vw_CustomerTickets TO booking_service;
-- Фізичного видалення глядачів, бронювань і квитків немає — лише soft delete / скасування;
-- записи про оплату незмінні (лише INSERT)
DENY DELETE ON dbo.Customers       TO booking_service;
DENY DELETE ON dbo.Bookings        TO booking_service;
DENY DELETE ON dbo.Tickets         TO booking_service;
DENY UPDATE, DELETE ON dbo.BookingPayments TO booking_service;

ALTER ROLE booking_service ADD MEMBER booking_app;
GO
-- У CinemaCatalog користувача booking_app немає → доступу до чужої БД немає.
