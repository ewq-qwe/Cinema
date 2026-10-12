/* =====================================================================
   Скрипт 006: перевірки — CRUD, пошук, пагінація, обмеження, переходи статусів,
   конкурентність, права доступу. Зміни, що лише демонструють помилки, відкочуються.
   ===================================================================== */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
USE CinemaBooking;
GO
SET NOCOUNT ON;

PRINT N'=== 1. CRUD глядача (прямий SQL) ===';
BEGIN TRANSACTION;
    INSERT dbo.Customers (FullName, Email, CreatedBy) VALUES (N'Тестовий Глядач', N'test@example.com', N'verify');   -- CREATE
    DECLARE @Id BIGINT = SCOPE_IDENTITY();
    SELECT Id, FullName, Email FROM dbo.Customers WHERE Id = @Id;                                                    -- READ
    DECLARE @rv BINARY(8) = (SELECT RowVersion FROM dbo.Customers WHERE Id = @Id);
    UPDATE dbo.Customers SET FullName = N'Тестовий Глядач 2', UpdatedAt = SYSUTCDATETIME() WHERE Id = @Id AND RowVersion = @rv;  -- UPDATE
    PRINT CONCAT(N'UPDATE з актуальним RowVersion: ', @@ROWCOUNT, N' рядок');
    UPDATE dbo.Customers SET FullName = N'Затерти чужі зміни' WHERE Id = @Id AND RowVersion = @rv;
    PRINT CONCAT(N'UPDATE із застарілим RowVersion: ', @@ROWCOUNT, N' рядків — конфлікт виявлено');
    UPDATE dbo.Customers SET IsDeleted = 1 WHERE Id = @Id;                                                           -- DELETE (soft)
    PRINT N'Soft delete виконано';
ROLLBACK TRANSACTION;
GO

PRINT N'=== 2. Пошук і пагінація ===';
EXEC dbo.usp_Bookings_GetPage @PageNumber = 1, @PageSize = 2;
EXEC dbo.usp_Bookings_GetPage @CustomerId = 1;
EXEC dbo.usp_Bookings_GetPage @Status = 'Paid';
GO

PRINT N'=== 3. Картка бронювання та схема залу ===';
EXEC dbo.usp_Booking_GetById @BookingId = 1;
EXEC dbo.usp_Session_GetSeatMap @SessionId = 1;
GO

PRINT N'=== 4. Обмеження цілісності (кожна спроба має впасти) ===';
BEGIN TRY INSERT dbo.Customers (FullName, Email) VALUES (N'Дубль', N'olena.koval@example.com'); END TRY
BEGIN CATCH PRINT CONCAT(N'UNIQUE -> ', ERROR_MESSAGE()); END CATCH;
BEGIN TRY INSERT dbo.Customers (FullName, Email) VALUES (N'Без пошти', N'not-an-email'); END TRY
BEGIN CATCH PRINT CONCAT(N'CHECK  -> ', ERROR_MESSAGE()); END CATCH;
BEGIN TRY INSERT dbo.Sessions (MovieId, HallId, StartsAt, BasePrice) VALUES (999, 1, '2030-01-01', 100); END TRY
BEGIN CATCH PRINT CONCAT(N'FK     -> ', ERROR_MESSAGE()); END CATCH;
BEGIN TRY UPDATE dbo.Sessions SET BasePrice = 0 WHERE Id = 1; END TRY
BEGIN CATCH PRINT CONCAT(N'CHECK  -> ', ERROR_MESSAGE()); END CATCH;
BEGIN TRY UPDATE dbo.Bookings SET Status = 'Shipped' WHERE Id = 1; END TRY
BEGIN CATCH PRINT CONCAT(N'CHECK  -> ', ERROR_MESSAGE()); END CATCH;
BEGIN TRY DELETE dbo.Customers WHERE Id = 1; END TRY
BEGIN CATCH PRINT CONCAT(N'FK (бронювання не видаляються каскадом) -> ', ERROR_MESSAGE()); END CATCH;
GO

PRINT N'=== 5. Процедури відмовляють керованою помилкою ===';
DECLARE @Seats dbo.SeatIdList, @BookingId BIGINT;

-- місце вже продане (ряд 3, місце 4 на сеансі 1 — у бронюванні №1)
INSERT @Seats SELECT Id FROM dbo.Seats WHERE HallId = 1 AND RowNumber = 3 AND SeatNumber = 4;
BEGIN TRY EXEC dbo.usp_Booking_Create 2, 1, @Seats, @BookingId = @BookingId OUTPUT; END TRY
BEGIN CATCH PRINT CONCAT(ERROR_NUMBER(), N' -> ', ERROR_MESSAGE()); END CATCH;

-- місце з іншого залу
DELETE @Seats; INSERT @Seats SELECT Id FROM dbo.Seats WHERE HallId = 2 AND RowNumber = 1 AND SeatNumber = 1;
BEGIN TRY EXEC dbo.usp_Booking_Create 2, 1, @Seats, @BookingId = @BookingId OUTPUT; END TRY
BEGIN CATCH PRINT CONCAT(ERROR_NUMBER(), N' -> ', ERROR_MESSAGE()); END CATCH;

-- «використати» неоплачене бронювання (№2 у статусі Created) — недопустимий перехід
BEGIN TRY EXEC dbo.usp_Booking_ChangeStatus @BookingId = 2, @NewStatus = 'Used'; END TRY
BEGIN CATCH PRINT CONCAT(ERROR_NUMBER(), N' -> ', ERROR_MESSAGE()); END CATCH;

-- скасувати вже використане бронювання (№3, Used)
BEGIN TRY EXEC dbo.usp_Booking_ChangeStatus @BookingId = 3, @NewStatus = 'Cancelled'; END TRY
BEGIN CATCH PRINT CONCAT(ERROR_NUMBER(), N' -> ', ERROR_MESSAGE()); END CATCH;

-- повторна оплата вже оплаченого (№1, Paid)
BEGIN TRY EXEC dbo.usp_Booking_Pay @BookingId = 1, @Amount = 300, @Method = 'card', @TransactionId = 'X-1'; END TRY
BEGIN CATCH PRINT CONCAT(ERROR_NUMBER(), N' -> ', ERROR_MESSAGE()); END CATCH;

-- неправильна сума оплати (№2 коштує 225)
BEGIN TRY EXEC dbo.usp_Booking_Pay @BookingId = 2, @Amount = 1, @Method = 'card', @TransactionId = 'X-2'; END TRY
BEGIN CATCH PRINT CONCAT(ERROR_NUMBER(), N' -> ', ERROR_MESSAGE()); END CATCH;

-- оптимістична конкурентність: застарілий RowVersion
BEGIN TRY EXEC dbo.usp_Booking_ChangeStatus @BookingId = 2, @NewStatus = 'Cancelled', @ExpectedRowVersion = 0x0000000000000001; END TRY
BEGIN CATCH PRINT CONCAT(ERROR_NUMBER(), N' -> ', ERROR_MESSAGE()); END CATCH;
GO

PRINT N'=== 6. Транзакція: успішне бронювання -> оплата -> скасування (і відкат усього) ===';
BEGIN TRANSACTION;
    DECLARE @Seats dbo.SeatIdList, @BookingId BIGINT, @Total DECIMAL(10,2);
    INSERT @Seats SELECT Id FROM dbo.Seats WHERE HallId = 1 AND RowNumber = 5 AND SeatNumber IN (7, 8);   -- 2 VIP
    EXEC dbo.usp_Booking_Create 2, 2, @Seats, @BookingId = @BookingId OUTPUT;
    SELECT @Total = TotalAmount FROM dbo.Bookings WHERE Id = @BookingId;
    PRINT CONCAT(N'Створено бронювання #', @BookingId, N' на суму ', @Total, N' (VIP 180 × 1.5 × 2 = 540)');
    EXEC dbo.usp_Booking_Pay @BookingId, @Total, 'online', 'VERIFY-TXN-1';
    EXEC dbo.usp_Booking_ChangeStatus @BookingId, 'Cancelled', N'Перевірка';
    DECLARE @FreeAgain INT = (SELECT COUNT(*) FROM dbo.Tickets WHERE BookingId = @BookingId AND IsCancelled = 1);
    PRINT CONCAT(N'Після скасування звільнено місць: ', @FreeAgain);
ROLLBACK TRANSACTION;
GO

PRINT N'=== 7. Самоперевірка: усі FOREIGN KEY посилаються лише на таблиці цієї БД ===';
SELECT  fk.name AS ForeignKey,
        OBJECT_NAME(fk.parent_object_id)     AS FromTable,
        OBJECT_NAME(fk.referenced_object_id) AS ToTable
FROM    sys.foreign_keys fk
ORDER BY FromTable;
GO

PRINT N'=== 8. Права: логін booking_app бачить лише свою БД ===';
EXECUTE AS LOGIN = 'booking_app';
    SELECT SUSER_SNAME() AS LoginName, DB_NAME() AS CurrentDb, (SELECT COUNT(*) FROM dbo.Movies) AS MoviesVisible;
    BEGIN TRY DELETE dbo.Customers WHERE Id = 999; END TRY
    BEGIN CATCH PRINT CONCAT(N'DELETE заборонено -> ', ERROR_MESSAGE()); END CATCH;
REVERT;
GO
