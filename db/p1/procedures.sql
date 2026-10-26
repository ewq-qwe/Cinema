/* =====================================================================
   procedures.sql — 6 збережуваних процедур БД CinemaBooking
     CRUD:   1. usp_Customer_Save   2. usp_Customer_Delete   6. usp_Booking_GetById (READ)
     Бізнес: 3. usp_Booking_Create  4. usp_Booking_Pay       5. usp_Booking_ChangeStatus

   Статуси бронювання і допустимі переходи:
       Created ──► Paid ──► Used
          │          │
          └──► Cancelled ◄┘   (скасування — лише до початку сеансу)

   Коди помилок (THROW 50xxx) — керовані бізнес-помилки:
     50001 не знайдено          50002 недопустимий перехід статусу
     50003 сеанс уже почався    50004 місце недоступне / зайняте
     50005 неправильна сума     50006 конфлікт конкурентності (RowVersion)
     50007 видалення неможливе (є активні бронювання)
   ===================================================================== */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
USE CinemaBooking;
GO

/* ---------------------------------------------------------------------
   1. usp_Customer_Save — CREATE (якщо @Id = NULL) або UPDATE глядача
      UPDATE з оптимістичною конкурентністю: @ExpectedRowVersion — версія, яку бачив користувач
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.usp_Customer_Save
    @Id                  BIGINT        = NULL OUTPUT,
    @FullName            NVARCHAR(150),
    @Email               NVARCHAR(254),
    @Phone               VARCHAR(13)   = NULL,
    @ExpectedRowVersion  BINARY(8)     = NULL,
    @UserName            NVARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @Id IS NULL
    BEGIN
        INSERT dbo.Customers (FullName, Email, Phone, CreatedBy)
        VALUES (@FullName, @Email, @Phone, @UserName);
        SET @Id = SCOPE_IDENTITY();
        RETURN 0;
    END;

    UPDATE dbo.Customers
    SET    FullName = @FullName, Email = @Email, Phone = @Phone,
           UpdatedAt = SYSUTCDATETIME(), UpdatedBy = @UserName
    WHERE  Id = @Id AND IsDeleted = 0
      AND  (@ExpectedRowVersion IS NULL OR RowVersion = @ExpectedRowVersion);

    IF @@ROWCOUNT = 0
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM dbo.Customers WHERE Id = @Id AND IsDeleted = 0)
            THROW 50001, N'Глядача не знайдено.', 1;
        THROW 50006, N'Дані глядача змінено іншим користувачем. Оновіть дані.', 1;
    END;
END;
GO

/* ---------------------------------------------------------------------
   2. usp_Customer_Delete — DELETE (м'яке): IsDeleted = 1, історія бронювань зберігається
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.usp_Customer_Delete
    @Id        BIGINT,
    @UserName  NVARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRANSACTION;

        IF NOT EXISTS (SELECT 1 FROM dbo.Customers WITH (UPDLOCK) WHERE Id = @Id AND IsDeleted = 0)
            THROW 50001, N'Глядача не знайдено.', 1;

        -- Не можна видалити глядача, у якого є неоплачені або оплачені бронювання на майбутні сеанси
        IF EXISTS (SELECT 1 FROM dbo.Bookings
                   WHERE CustomerId = @Id AND Status IN ('Created', 'Paid') AND SessionStartsAt > SYSUTCDATETIME())
            THROW 50007, N'У глядача є активні бронювання — спершу скасуйте їх.', 1;

        UPDATE dbo.Customers
        SET    IsDeleted = 1, UpdatedAt = SYSUTCDATETIME(), UpdatedBy = @UserName
        WHERE  Id = @Id;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
GO

/* ---------------------------------------------------------------------
   3. usp_Booking_Create — бронювання місць на сеанс (бізнес-операція, транзакція)
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.usp_Booking_Create
    @CustomerId  BIGINT,
    @SessionId   BIGINT,
    @Seats       dbo.SeatIdList READONLY,
    @UserName    NVARCHAR(100) = NULL,
    @BookingId   BIGINT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;                      -- будь-яка помилка відкочує транзакцію

    BEGIN TRY
        BEGIN TRANSACTION;

        IF NOT EXISTS (SELECT 1 FROM dbo.Customers WHERE Id = @CustomerId AND IsDeleted = 0)
            THROW 50001, N'Глядача не знайдено.', 1;

        DECLARE @HallId INT, @StartsAt DATETIME2(0), @BasePrice DECIMAL(10,2), @Title NVARCHAR(200);
        SELECT  @HallId = s.HallId, @StartsAt = s.StartsAt, @BasePrice = s.BasePrice, @Title = m.Title
        FROM    dbo.Sessions s
        JOIN    dbo.Movies m ON m.Id = s.MovieId
        WHERE   s.Id = @SessionId;

        IF @HallId IS NULL
            THROW 50001, N'Сеанс не знайдено.', 1;
        IF @StartsAt <= SYSUTCDATETIME()
            THROW 50003, N'Сеанс уже почався — бронювання неможливе.', 1;
        IF NOT EXISTS (SELECT 1 FROM @Seats)
            THROW 50004, N'Не вибрано жодного місця.', 1;

        -- Усі місця мають належати залу цього сеансу
        IF EXISTS (SELECT 1 FROM @Seats x LEFT JOIN dbo.Seats s ON s.Id = x.SeatId AND s.HallId = @HallId
                   WHERE s.Id IS NULL)
            THROW 50004, N'Одне або кілька місць не належать залу цього сеансу.', 1;

        -- UPDLOCK + HOLDLOCK: два глядачі не викуплять одне місце одночасно
        IF EXISTS (SELECT 1 FROM dbo.Tickets t WITH (UPDLOCK, HOLDLOCK)
                   JOIN @Seats x ON x.SeatId = t.SeatId
                   WHERE t.SessionId = @SessionId AND t.IsCancelled = 0)
            THROW 50004, N'Одне або кілька місць уже зайняті.', 1;

        INSERT dbo.Bookings (CustomerId, SessionId, Status, MovieTitle, SessionStartsAt, TotalAmount, CreatedBy)
        VALUES (@CustomerId, @SessionId, 'Created', @Title, @StartsAt, 0, @UserName);
        SET @BookingId = SCOPE_IDENTITY();

        -- VIP-місце дорожче в 1.5 раза; ціна фіксується в квитку (знімок)
        INSERT dbo.Tickets (BookingId, SeatId, SessionId, Price)
        SELECT @BookingId, s.Id, @SessionId,
               CASE s.SeatType WHEN 'VIP' THEN ROUND(@BasePrice * 1.5, 2) ELSE @BasePrice END
        FROM   @Seats x
        JOIN   dbo.Seats s ON s.Id = x.SeatId;

        UPDATE dbo.Bookings
        SET    TotalAmount = (SELECT SUM(Price) FROM dbo.Tickets WHERE BookingId = @BookingId)
        WHERE  Id = @BookingId;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
GO

/* ---------------------------------------------------------------------
   4. usp_Booking_Pay — оплата: Created -> Paid + запис 1:1 у BookingPayments
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.usp_Booking_Pay
    @BookingId      BIGINT,
    @Amount         DECIMAL(10,2),
    @Method         VARCHAR(10),
    @TransactionId  VARCHAR(64),
    @UserName       NVARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRANSACTION;

        DECLARE @Status VARCHAR(20), @Total DECIMAL(10,2), @StartsAt DATETIME2(0);
        SELECT  @Status = Status, @Total = TotalAmount, @StartsAt = SessionStartsAt
        FROM    dbo.Bookings WITH (UPDLOCK)        -- блокуємо рядок до кінця транзакції
        WHERE   Id = @BookingId AND IsDeleted = 0;

        IF @Status IS NULL
            THROW 50001, N'Бронювання не знайдено.', 1;
        IF @Status <> 'Created'
            THROW 50002, N'Оплатити можна лише нове (неоплачене) бронювання.', 1;
        IF @StartsAt <= SYSUTCDATETIME()
            THROW 50003, N'Сеанс уже почався.', 1;
        IF @Amount <> @Total
            THROW 50005, N'Сума оплати не збігається з вартістю бронювання.', 1;

        INSERT dbo.BookingPayments (BookingId, Amount, Method, TransactionId)
        VALUES (@BookingId, @Amount, @Method, @TransactionId);

        UPDATE dbo.Bookings
        SET    Status = 'Paid', UpdatedAt = SYSUTCDATETIME(), UpdatedBy = @UserName
        WHERE  Id = @BookingId;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
GO

/* ---------------------------------------------------------------------
   5. usp_Booking_ChangeStatus — зміна статусу з перевіркою переходу
      * Paid -> Used         (контролер на вході в зал)
      * Created/Paid -> Cancelled (лише до початку сеансу; квитки звільняються)
      * усе інше — керована помилка 50002
      * @ExpectedRowVersion (необов'язковий) — оптимістична конкурентність
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.usp_Booking_ChangeStatus
    @BookingId           BIGINT,
    @NewStatus           VARCHAR(20),
    @Reason              NVARCHAR(300) = NULL,
    @ExpectedRowVersion  BINARY(8)     = NULL,
    @UserName            NVARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRANSACTION;

        DECLARE @Status VARCHAR(20), @StartsAt DATETIME2(0), @RowVersion BINARY(8);
        SELECT  @Status = Status, @StartsAt = SessionStartsAt, @RowVersion = RowVersion
        FROM    dbo.Bookings WITH (UPDLOCK)
        WHERE   Id = @BookingId AND IsDeleted = 0;

        IF @Status IS NULL
            THROW 50001, N'Бронювання не знайдено.', 1;
        IF @ExpectedRowVersion IS NOT NULL AND @ExpectedRowVersion <> @RowVersion
            THROW 50006, N'Бронювання змінено іншим користувачем. Оновіть дані.', 1;

        -- Граф допустимих переходів
        IF NOT (   (@Status = 'Paid'    AND @NewStatus = 'Used')
                OR (@Status = 'Created' AND @NewStatus = 'Cancelled')
                OR (@Status = 'Paid'    AND @NewStatus = 'Cancelled'))
        BEGIN
            DECLARE @Msg NVARCHAR(200) = CONCAT(N'Недопустимий перехід статусу: ', @Status, N' -> ', @NewStatus, N'.');
            THROW 50002, @Msg, 1;
        END;

        IF @NewStatus = 'Cancelled' AND @StartsAt <= SYSUTCDATETIME()
            THROW 50003, N'Сеанс уже почався — скасування неможливе.', 1;

        UPDATE dbo.Bookings
        SET    Status = @NewStatus,
               CancelReason = IIF(@NewStatus = 'Cancelled', @Reason, CancelReason),
               UpdatedAt = SYSUTCDATETIME(), UpdatedBy = @UserName
        WHERE  Id = @BookingId;

        IF @NewStatus = 'Cancelled'
            UPDATE dbo.Tickets SET IsCancelled = 1 WHERE BookingId = @BookingId;   -- місця знову вільні

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;
END;
GO

/* ---------------------------------------------------------------------
   6. usp_Booking_GetById — картка бронювання (READ): 2 набори результатів
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.usp_Booking_GetById
    @BookingId BIGINT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT  b.Id, b.Status, b.MovieTitle, b.SessionStartsAt, b.TotalAmount, b.CancelReason,
            b.CreatedAt, b.RowVersion, c.FullName AS CustomerName, c.Email,
            p.Method AS PaymentMethod, p.PaidAt
    FROM    dbo.Bookings b
    JOIN    dbo.Customers c ON c.Id = b.CustomerId
    LEFT JOIN dbo.BookingPayments p ON p.BookingId = b.Id
    WHERE   b.Id = @BookingId AND b.IsDeleted = 0;

    SELECT  h.Name AS HallName, s.RowNumber, s.SeatNumber, s.SeatType, t.Price, t.IsCancelled
    FROM    dbo.Tickets t
    JOIN    dbo.Seats s ON s.Id = t.SeatId
    JOIN    dbo.Halls h ON h.Id = s.HallId
    WHERE   t.BookingId = @BookingId
    ORDER BY s.RowNumber, s.SeatNumber;
END;
GO

