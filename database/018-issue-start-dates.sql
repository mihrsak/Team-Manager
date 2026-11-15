SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE Version=18)
BEGIN
    ALTER TABLE dbo.Issues ADD StartDate date NULL;
    EXEC(N'ALTER TABLE dbo.Issues ADD CONSTRAINT CK_Issues_Schedule
        CHECK (StartDate IS NULL OR DueDate IS NULL OR StartDate<=DueDate)');
    INSERT dbo.SchemaVersions(Version) VALUES (18);
END;
COMMIT;
