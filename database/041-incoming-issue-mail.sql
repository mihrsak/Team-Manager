SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE Version=41)
BEGIN
    CREATE TABLE dbo.ReceivedIssueMail (
        ProjectId int NOT NULL REFERENCES dbo.Projects(Id),
        MessageHash binary(32) NOT NULL,
        IssueId int NOT NULL REFERENCES dbo.Issues(Id),
        ReceivedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
        PRIMARY KEY(ProjectId,MessageHash)
    );
    INSERT dbo.SchemaVersions(Version) VALUES (41);
END;
COMMIT;
