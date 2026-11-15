SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF COL_LENGTH('dbo.AppUsers', 'IsActive') IS NULL
    ALTER TABLE dbo.AppUsers ADD IsActive bit NOT NULL CONSTRAINT DF_AppUsers_IsActive DEFAULT (1);
IF COL_LENGTH('dbo.AppUsers', 'SessionVersion') IS NULL
    ALTER TABLE dbo.AppUsers ADD SessionVersion int NOT NULL CONSTRAINT DF_AppUsers_SessionVersion DEFAULT (0);
COMMIT;
