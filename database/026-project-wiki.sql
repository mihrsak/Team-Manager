SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE Version=26)
BEGIN
    CREATE TABLE dbo.ProjectWikiPages (
        Id int IDENTITY PRIMARY KEY,
        ProjectId int NOT NULL REFERENCES dbo.Projects(Id),
        Title nvarchar(120) NOT NULL,
        Revision int NOT NULL DEFAULT (1),
        CONSTRAINT UQ_ProjectWikiPages_Title UNIQUE(ProjectId,Title)
    );
    CREATE TABLE dbo.ProjectWikiRevisions (
        PageId int NOT NULL REFERENCES dbo.ProjectWikiPages(Id),
        Revision int NOT NULL,
        Title nvarchar(120) NOT NULL,
        Body nvarchar(max) NOT NULL,
        AuthorId int NOT NULL REFERENCES dbo.AppUsers(Id),
        CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
        PRIMARY KEY(PageId,Revision)
    );
    INSERT dbo.RolePermissions(RoleName,Permission)
    SELECT r.Name,'wiki.edit' FROM dbo.Roles r
    WHERE r.Name IN (N'Voditelj',N'Developer')
        AND NOT EXISTS (SELECT 1 FROM dbo.RolePermissions p WHERE p.RoleName=r.Name AND p.Permission='wiki.edit');
    INSERT dbo.SchemaVersions(Version) VALUES (26);
END;
COMMIT;
