SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE Version=3)
BEGIN
    CREATE TABLE dbo.OrganizationUnits (Id int IDENTITY PRIMARY KEY, Name nvarchar(100) NOT NULL UNIQUE);
    CREATE TABLE dbo.AppUsers (
        Id int IDENTITY PRIMARY KEY,
        EmployeeNumber varchar(30) NOT NULL UNIQUE,
        DisplayName nvarchar(100) NOT NULL,
        JobTitle nvarchar(100) NOT NULL DEFAULT N'',
        OrganizationUnitId int NULL REFERENCES dbo.OrganizationUnits(Id),
        PasswordHash nvarchar(500) NOT NULL,
        IsAdministrator bit NOT NULL DEFAULT 0,
        FailedAttempts int NOT NULL DEFAULT 0,
        LockoutUntil datetime2 NULL,
        CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
    ALTER TABLE dbo.Members ADD UserId int NULL REFERENCES dbo.AppUsers(Id), Role nvarchar(30) NOT NULL CONSTRAINT DF_MemberRole DEFAULT N'Član';
    EXEC(N'CREATE UNIQUE INDEX UX_Members_UserTeam ON dbo.Members(TeamId,UserId) WHERE UserId IS NOT NULL');
    EXEC(N'ALTER TABLE dbo.Members ADD CONSTRAINT CK_MemberRole CHECK (Role IN (N''Član'', N''Voditelj'', N''Promatrač''))');
    CREATE TABLE dbo.Projects (Id int IDENTITY PRIMARY KEY, Name nvarchar(100) NOT NULL UNIQUE);
    CREATE TABLE dbo.ProjectMembers (
        ProjectId int NOT NULL REFERENCES dbo.Projects(Id),
        UserId int NOT NULL REFERENCES dbo.AppUsers(Id),
        Role nvarchar(30) NOT NULL,
        PRIMARY KEY(ProjectId,UserId),
        CONSTRAINT CK_ProjectRole CHECK(Role IN (N'Član', N'Voditelj', N'Promatrač'))
    );
    INSERT dbo.SchemaVersions(Version) VALUES(3);
END;
COMMIT;
