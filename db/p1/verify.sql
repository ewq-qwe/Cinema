/* =====================================================================
   verify.sql — перевірки — CRUD, пошук, пагінація, обмеження, переходи статусів,
   конкурентність, права доступу. Зміни, що лише демонструють помилки, відкочуються.
   ===================================================================== */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
USE CinemaBooking;
GO
SET NOCOUNT ON;

PRINT N'=== 1. CRUD глядача через процедури usp_Customer_Save / usp_Customer_Delete ===';
    DECLARE @Id BIGINT, @rv BINARY(8);
    EXEC dbo.usp_Customer_Save @Id = @Id OUTPUT, @FullName = N'Тестовий Глядач', @Email = N'test@example.com', @UserName = N'verify';  -- CREATE
    SELECT Id, FullName, Email, RowVersion FROM dbo.Customers WHERE Id = @Id;                                                           -- READ
    SELECT @rv = RowVersion FROM dbo.Customers WHERE Id = @Id;
    EXEC dbo.usp_Customer_Save @Id = @Id OUTPUT, @FullName = N'Тестовий Глядач 2', @Email = N'test@example.com', @ExpectedRowVersion = @rv;  -- UPDATE
    PRINT N'UPDATE з актуальним RowVersion — OK';
    BEGIN TRY
        EXEC dbo.usp_Customer_Save @Id = @Id OUTPUT, @FullName = N'Затерти чужі зміни', @Email = N'test@example.com', @ExpectedRowVersion = @rv;
    END TRY
    BEGIN CATCH PRINT CONCAT(ERROR_NUMBER(), N' -> ', ERROR_MESSAGE(), N'  (застарілий RowVersion)'); END CATCH;
    EXEC dbo.usp_Customer_Delete @Id = @Id;                                                                                             -- DELETE (soft)
    SELECT Id, FullName, IsDeleted FROM dbo.Customers WHERE Id = @Id;
    BEGIN TRY EXEC dbo.usp_Customer_Delete @Id = 1; END TRY                       -- у Олени є активне бронювання
    BEGIN CATCH PRINT CONCAT(ERROR_NUMBER(), N' -> ', ERROR_MESSAGE()); END CATCH;
    DELETE dbo.Customers WHERE Id = @Id;                                          -- прибрати тестовий запис (від імені sa)
GO

PRINT N'=== 2. Картка бронювання (usp_Booking_GetById) ===';
EXEC dbo.usp_Booking_GetById @BookingId = 1;
GO

PRINT N'=== 3. Пошук і пагінація — див. queries.sql (виконується окремо) ===';
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
