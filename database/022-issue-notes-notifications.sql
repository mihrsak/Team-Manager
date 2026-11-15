SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE Version=22)
BEGIN
    CREATE TABLE dbo.IssuePrivateNotes (
        Id int IDENTITY PRIMARY KEY,
        IssueId int NOT NULL REFERENCES dbo.Issues(Id) ON DELETE CASCADE,
        UserId int NOT NULL REFERENCES dbo.AppUsers(Id),
        Body nvarchar(4000) NOT NULL,
        CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_IssuePrivateNotes_UserIssue ON dbo.IssuePrivateNotes(UserId,IssueId,Id);

    CREATE TABLE dbo.IssueNotifications (
        EventId int NOT NULL REFERENCES dbo.IssueEvents(Id),
        IssueId int NOT NULL,
        UserId int NOT NULL,
        IsRead bit NOT NULL DEFAULT (0),
        PRIMARY KEY (UserId,EventId),
        FOREIGN KEY (IssueId,UserId) REFERENCES dbo.IssueWatchers(IssueId,UserId) ON DELETE CASCADE
    );
    CREATE INDEX IX_IssueNotifications_Watcher ON dbo.IssueNotifications(IssueId,UserId);
    INSERT dbo.SchemaVersions(Version) VALUES (22);
END;
COMMIT;
