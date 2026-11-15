SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE Version=28)
BEGIN
    CREATE TABLE dbo.ProjectDocuments (
        Id int IDENTITY PRIMARY KEY,
        ProjectId int NOT NULL REFERENCES dbo.Projects(Id),
        AuthorId int NOT NULL REFERENCES dbo.AppUsers(Id),
        Title nvarchar(150) NOT NULL,
        Description nvarchar(2000) NOT NULL,
        FileName nvarchar(200) NOT NULL,
        Content varbinary(max) NOT NULL CHECK (DATALENGTH(Content) BETWEEN 1 AND 10485760),
        CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_ProjectDocuments_Project ON dbo.ProjectDocuments(ProjectId,Id DESC);
    INSERT dbo.SchemaVersions(Version) VALUES (28);
END;
COMMIT;
