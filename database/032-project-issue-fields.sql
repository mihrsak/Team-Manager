SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE Version=32)
BEGIN
    CREATE TABLE dbo.ProjectIssueFields (
        Id int IDENTITY PRIMARY KEY,
        ProjectId int NOT NULL REFERENCES dbo.Projects(Id),
        Name nvarchar(100) NOT NULL,
        CONSTRAINT UQ_ProjectIssueFields_Name UNIQUE(ProjectId,Name)
    );
    CREATE TABLE dbo.IssueFieldValues (
        IssueId int NOT NULL REFERENCES dbo.Issues(Id) ON DELETE CASCADE,
        FieldId int NOT NULL REFERENCES dbo.ProjectIssueFields(Id),
        Value nvarchar(500) NOT NULL,
        PRIMARY KEY(IssueId,FieldId)
    );
    INSERT dbo.SchemaVersions(Version) VALUES (32);
END;
COMMIT;
