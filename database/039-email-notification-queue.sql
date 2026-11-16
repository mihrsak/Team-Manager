SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE Version=39)
BEGIN
    CREATE TABLE dbo.EmailNotificationPreferences (
        UserId int NOT NULL PRIMARY KEY REFERENCES dbo.AppUsers(Id),
        Address nvarchar(254) NULL,
        SendWatchedIssues bit NOT NULL,
        CONSTRAINT CK_EmailNotificationPreferences_Address CHECK (SendWatchedIssues=0 OR Address IS NOT NULL)
    );
    CREATE TABLE dbo.IssueEmailQueue (
        UserId int NOT NULL,
        EventId int NOT NULL,
        QueuedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
        PRIMARY KEY (UserId,EventId),
        FOREIGN KEY (UserId,EventId) REFERENCES dbo.IssueNotifications(UserId,EventId) ON DELETE CASCADE
    );
    CREATE INDEX IX_IssueEmailQueue_Pending ON dbo.IssueEmailQueue(QueuedAt,UserId,EventId);
    INSERT dbo.SchemaVersions(Version) VALUES (39);
END;
COMMIT;
