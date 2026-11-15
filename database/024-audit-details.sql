SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE Version=24)
BEGIN
    ALTER TABLE dbo.AdministrationAudit ADD Details nvarchar(2000) NOT NULL
        CONSTRAINT DF_AdministrationAudit_Details DEFAULT N'';
    INSERT dbo.SchemaVersions(Version) VALUES (24);
END;
COMMIT;
