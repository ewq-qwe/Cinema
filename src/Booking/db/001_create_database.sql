/* =====================================================================
   Проєкт №1. Мікросервіс «Бронювання квитків» кінотеатру «Кадр»
   Скрипт 001: створення бази даних CinemaBooking

   Навчальне середовище: БД створюється з нуля при кожному розгортанні.
   (У production замість цього — інкрементальні скрипти змін.)
   ===================================================================== */
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF DB_ID(N'CinemaBooking') IS NOT NULL
BEGIN
    ALTER DATABASE CinemaBooking SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE CinemaBooking;
END;
GO

CREATE DATABASE CinemaBooking COLLATE Ukrainian_100_CI_AS;
GO
