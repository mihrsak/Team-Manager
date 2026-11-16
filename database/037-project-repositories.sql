SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE Version=37)
BEGIN
    CREATE TABLE dbo.ProjectRepositories (
        Id int IDENTITY PRIMARY KEY,
        ProjectId int NOT NULL REFERENCES dbo.Projects(Id),
        Name nvarchar(80) NOT NULL,
        Url nvarchar(500) NOT NULL,
        UNIQUE (ProjectId,Name)
    );
    INSERT dbo.SchemaVersions(Version) VALUES (37);
END;
COMMIT;
