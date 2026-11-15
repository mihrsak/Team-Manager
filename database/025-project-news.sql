SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE Version=25)
BEGIN
    CREATE TABLE dbo.ProjectNews (
        Id int IDENTITY PRIMARY KEY,
        ProjectId int NOT NULL REFERENCES dbo.Projects(Id),
        AuthorId int NOT NULL REFERENCES dbo.AppUsers(Id),
        Title nvarchar(150) NOT NULL,
        Body nvarchar(4000) NOT NULL,
        CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_ProjectNews_Project ON dbo.ProjectNews(ProjectId,Id DESC);
    CREATE TABLE dbo.ProjectNewsComments (
        Id int IDENTITY PRIMARY KEY,
        NewsId int NOT NULL REFERENCES dbo.ProjectNews(Id),
        AuthorId int NOT NULL REFERENCES dbo.AppUsers(Id),
        Body nvarchar(2000) NOT NULL,
        CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_ProjectNewsComments_News ON dbo.ProjectNewsComments(NewsId,Id);
    INSERT dbo.RolePermissions(RoleName,Permission)
    SELECT Name,'news.comment' FROM dbo.Roles
    WHERE Name IN (N'Voditelj',N'Developer',N'Moderator',N'Član')
        AND NOT EXISTS (SELECT 1 FROM dbo.RolePermissions WHERE RoleName=Name AND Permission='news.comment');
    INSERT dbo.SchemaVersions(Version) VALUES (25);
END;
COMMIT;
