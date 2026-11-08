-- Pokrenuti s ovlastima za kreiranje baze, samo ako baza jos ne postoji.
USE master;
GO
IF DB_ID(N'TeamManager') IS NULL
    CREATE DATABASE TeamManager;
GO
