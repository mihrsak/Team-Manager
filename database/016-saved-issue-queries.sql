SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE Version=16)
BEGIN
    CREATE TABLE dbo.SavedIssueQueries (
        Id int IDENTITY PRIMARY KEY,
        UserId int NOT NULL REFERENCES dbo.AppUsers(Id),
        Name nvarchar(80) NOT NULL,
        ProjectId int NULL REFERENCES dbo.Projects(Id),
        Status nvarchar(30) NULL REFERENCES dbo.IssueStatuses(Name),
        SearchText nvarchar(200) NULL,
        CONSTRAINT UQ_SavedIssueQueries_User_Name UNIQUE(UserId,Name)
    );
    INSERT dbo.SchemaVersions(Version) VALUES (16);
END;
COMMIT;
