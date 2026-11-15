SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE Version=17)
BEGIN
    ALTER TABLE dbo.SavedIssueQueries ADD SortOrder varchar(20) NULL,
        GroupBy varchar(20) NULL, Columns varchar(100) NULL;
    INSERT dbo.SchemaVersions(Version) VALUES (17);
END;
COMMIT;
