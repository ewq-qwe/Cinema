/* =====================================================================
   queries.sql — типові запити для перевірки: пошук, пагінація, схема залу, звіт.
   (Це звичайні SELECT-и; у Лаб. 02 їх виконуватиме DAL через Dapper.)
   ===================================================================== */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO
USE CinemaBooking;
GO

-- 1. Пагінація: бронювання глядача, нові спочатку (індекс IX_Bookings_Customer)
DECLARE @CustomerId BIGINT = 1, @PageNumber INT = 1, @PageSize INT = 10;
SELECT  b.Id, b.MovieTitle, b.SessionStartsAt, b.Status, b.TotalAmount,
        COUNT(*) OVER () AS TotalCount
FROM    dbo.Bookings b
WHERE   b.CustomerId = @CustomerId AND b.IsDeleted = 0
ORDER BY b.CreatedAt DESC, b.Id DESC
OFFSET (@PageNumber - 1) * @PageSize ROWS FETCH NEXT @PageSize ROWS ONLY;

-- 2. Пошук глядача за частиною імені або email
DECLARE @Term NVARCHAR(100) = N'%ко%';
SELECT Id, FullName, Email FROM dbo.Customers
WHERE  IsDeleted = 0 AND (FullName LIKE @Term OR Email LIKE @Term)
ORDER BY FullName;

-- 3. Розклад: сеанси фільму, що ще не почались (індекс IX_Sessions_Movie_StartsAt)
DECLARE @MovieId BIGINT = 1;
SELECT s.Id, m.Title, h.Name AS Hall, s.StartsAt, s.BasePrice
FROM   dbo.Sessions s
JOIN   dbo.Movies m ON m.Id = s.MovieId
JOIN   dbo.Halls h  ON h.Id = s.HallId
WHERE  s.MovieId = @MovieId AND s.StartsAt > SYSUTCDATETIME()
ORDER BY s.StartsAt;

-- 4. Схема залу на сеанс: вільні/зайняті місця (індекс UX_Tickets_SessionSeat_Active)
DECLARE @SessionId BIGINT = 1;
SELECT  s.RowNumber,
        STRING_AGG(CONCAT(s.SeatNumber, IIF(t.SeatId IS NULL, N'', N'×'), IIF(s.SeatType = 'VIP', N'v', N'')), N' ')
            WITHIN GROUP (ORDER BY s.SeatNumber) AS Seats      -- × зайняте, v VIP
FROM    dbo.Sessions ss
JOIN    dbo.Seats s ON s.HallId = ss.HallId
LEFT JOIN dbo.Tickets t ON t.SessionId = ss.Id AND t.SeatId = s.Id AND t.IsCancelled = 0
WHERE   ss.Id = @SessionId
GROUP BY s.RowNumber
ORDER BY s.RowNumber;

-- 5. Неоплачені бронювання сеансу для каси (фільтрований індекс IX_Bookings_Session_Unpaid)
SELECT b.Id, c.FullName, b.TotalAmount, b.CreatedAt
FROM   dbo.Bookings b JOIN dbo.Customers c ON c.Id = b.CustomerId
WHERE  b.SessionId = @SessionId AND b.Status = 'Created';

-- 6. Денормалізоване представлення для читання: квитки глядача
SELECT * FROM dbo.vw_CustomerTickets WHERE Email = N'olena.koval@example.com';
GO
