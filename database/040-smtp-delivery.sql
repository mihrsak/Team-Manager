SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE Version=40)
BEGIN
    ALTER TABLE dbo.EmailNotificationPreferences ADD SendDueReminders bit NOT NULL
        CONSTRAINT DF_EmailNotificationPreferences_Reminders DEFAULT (0);
    EXEC(N'ALTER TABLE dbo.EmailNotificationPreferences ADD CONSTRAINT CK_EmailNotificationPreferences_Reminders
        CHECK (SendDueReminders=0 OR Address IS NOT NULL)');

    ALTER TABLE dbo.IssueEmailQueue ADD
        Attempts int NOT NULL CONSTRAINT DF_IssueEmailQueue_Attempts DEFAULT (0),
        AvailableAt datetime2 NOT NULL CONSTRAINT DF_IssueEmailQueue_Available DEFAULT SYSUTCDATETIME(),
        LeaseId uniqueidentifier NULL,
        CompletedAt datetime2 NULL;
    EXEC(N'CREATE INDEX IX_IssueEmailQueue_Available ON dbo.IssueEmailQueue(AvailableAt,UserId,EventId)
        WHERE CompletedAt IS NULL');

    CREATE TABLE dbo.IssueReminderQueue (
        UserId int NOT NULL REFERENCES dbo.AppUsers(Id),
        IssueId int NOT NULL REFERENCES dbo.Issues(Id),
        DueDate date NOT NULL,
        Attempts int NOT NULL DEFAULT (0),
        AvailableAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
        LeaseId uniqueidentifier NULL,
        CompletedAt datetime2 NULL,
        PRIMARY KEY (UserId,IssueId,DueDate)
    );
    CREATE INDEX IX_IssueReminderQueue_Available ON dbo.IssueReminderQueue(AvailableAt,UserId,IssueId)
        WHERE CompletedAt IS NULL;
    INSERT dbo.SchemaVersions(Version) VALUES (40);
END;
COMMIT;
