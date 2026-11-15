SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE Version=35)
BEGIN
    ALTER TABLE dbo.ProjectIssueFields ADD ManagersOnly bit NOT NULL
        CONSTRAINT DF_ProjectIssueFields_ManagersOnly DEFAULT (0);
    INSERT dbo.SchemaVersions(Version) VALUES (35);
END;
COMMIT;
