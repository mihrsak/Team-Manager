SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE Version=21)
BEGIN
    CREATE TABLE dbo.IssueWatchers (
        IssueId int NOT NULL REFERENCES dbo.Issues(Id) ON DELETE CASCADE,
        UserId int NOT NULL REFERENCES dbo.AppUsers(Id),
        PRIMARY KEY (IssueId,UserId)
    );
    CREATE INDEX IX_IssueWatchers_User ON dbo.IssueWatchers(UserId,IssueId);
    INSERT dbo.SchemaVersions(Version) VALUES (21);
END;
COMMIT;
