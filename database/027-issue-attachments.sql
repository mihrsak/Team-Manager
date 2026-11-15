SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE Version=27)
BEGIN
    CREATE TABLE dbo.IssueAttachments (
        Id int IDENTITY PRIMARY KEY,
        IssueId int NOT NULL REFERENCES dbo.Issues(Id) ON DELETE CASCADE,
        AuthorId int NOT NULL REFERENCES dbo.AppUsers(Id),
        FileName nvarchar(200) NOT NULL,
        Content varbinary(max) NOT NULL CHECK (DATALENGTH(Content) BETWEEN 1 AND 10485760),
        CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_IssueAttachments_Issue ON dbo.IssueAttachments(IssueId,Id);
    INSERT dbo.SchemaVersions(Version) VALUES (27);
END;
COMMIT;
