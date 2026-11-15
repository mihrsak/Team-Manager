SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE Version=30)
BEGIN
    ALTER TABLE dbo.AppUsers ADD DisplayTheme varchar(6) NOT NULL
        CONSTRAINT DF_AppUsers_DisplayTheme DEFAULT ('light');
    EXEC(N'ALTER TABLE dbo.AppUsers ADD CONSTRAINT CK_AppUsers_DisplayTheme
        CHECK (DisplayTheme IN (''light'',''dark'',''system''))');
    INSERT dbo.SchemaVersions(Version) VALUES (30);
END;
COMMIT;
