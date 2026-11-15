SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE Version=31)
BEGIN
    ALTER TABLE dbo.AppUsers ADD UiLanguage varchar(2) NOT NULL
        CONSTRAINT DF_AppUsers_UiLanguage DEFAULT ('hr');
    EXEC(N'ALTER TABLE dbo.AppUsers ADD CONSTRAINT CK_AppUsers_UiLanguage CHECK (UiLanguage IN (''hr'',''en''))');
    INSERT dbo.SchemaVersions(Version) VALUES (31);
END;
COMMIT;
