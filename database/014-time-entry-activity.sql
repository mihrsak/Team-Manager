SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE Version=14)
BEGIN
    ALTER TABLE dbo.TimeEntries ADD Activity nvarchar(60) NOT NULL
        CONSTRAINT DF_TimeEntries_Activity DEFAULT (N'Općenito');
    INSERT dbo.SchemaVersions(Version) VALUES (14);
END;
COMMIT;
