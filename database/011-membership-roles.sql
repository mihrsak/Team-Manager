SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE Version=11)
BEGIN
    CREATE TABLE dbo.TeamMemberRoles (
        MemberId int NOT NULL REFERENCES dbo.Members(Id) ON DELETE CASCADE,
        RoleName nvarchar(30) NOT NULL REFERENCES dbo.Roles(Name),
        PRIMARY KEY (MemberId, RoleName)
    );
    CREATE TABLE dbo.ProjectMemberRoles (
        ProjectId int NOT NULL,
        UserId int NOT NULL,
        RoleName nvarchar(30) NOT NULL REFERENCES dbo.Roles(Name),
        PRIMARY KEY (ProjectId, UserId, RoleName),
        FOREIGN KEY (ProjectId, UserId) REFERENCES dbo.ProjectMembers(ProjectId, UserId) ON DELETE CASCADE
    );
    INSERT dbo.SchemaVersions(Version) VALUES (11);
END;
COMMIT;
