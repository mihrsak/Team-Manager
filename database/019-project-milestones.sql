SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE Version=19)
BEGIN
    CREATE TABLE dbo.ProjectMilestones (
        Id int IDENTITY PRIMARY KEY,
        ProjectId int NOT NULL REFERENCES dbo.Projects(Id),
        Kind varchar(12) NOT NULL CHECK (Kind IN ('version','milestone')),
        Name nvarchar(100) NOT NULL,
        Description nvarchar(1000) NOT NULL,
        TargetDate date NULL,
        IsComplete bit NOT NULL DEFAULT (0),
        Revision int NOT NULL DEFAULT (0),
        CONSTRAINT UQ_ProjectMilestones_Name UNIQUE (ProjectId,Kind,Name)
    );
    INSERT dbo.SchemaVersions(Version) VALUES (19);
END;
COMMIT;
