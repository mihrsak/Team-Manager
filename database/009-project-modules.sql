SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF NOT EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE Version=9)
BEGIN
    ALTER TABLE dbo.Projects ADD IssuesEnabled bit NOT NULL CONSTRAINT DF_Project_IssuesEnabled DEFAULT 1,
        TimeEnabled bit NOT NULL CONSTRAINT DF_Project_TimeEnabled DEFAULT 1;
    INSERT dbo.SchemaVersions(Version) VALUES(9);
END;
COMMIT;
