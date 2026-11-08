SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF NOT EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE Version=4)
BEGIN
    CREATE TABLE dbo.Roles(Name nvarchar(30) NOT NULL PRIMARY KEY, Description nvarchar(200) NOT NULL DEFAULT N'');
    CREATE TABLE dbo.RolePermissions(RoleName nvarchar(30) NOT NULL REFERENCES dbo.Roles(Name), Permission varchar(50) NOT NULL, PRIMARY KEY(RoleName,Permission));
    CREATE TABLE dbo.UserRoles(UserId int NOT NULL REFERENCES dbo.AppUsers(Id), RoleName nvarchar(30) NOT NULL REFERENCES dbo.Roles(Name), PRIMARY KEY(UserId,RoleName));
    INSERT dbo.Roles(Name) VALUES(N'Administrator'),(N'Moderator'),(N'Developer'),(N'Član'),(N'Voditelj'),(N'Promatrač');
    INSERT dbo.RolePermissions VALUES(N'Administrator','*'),(N'Moderator','members.manage'),(N'Voditelj','members.manage');
    INSERT dbo.UserRoles SELECT Id,N'Administrator' FROM dbo.AppUsers WHERE IsAdministrator=1;
    ALTER TABLE dbo.Members DROP CONSTRAINT CK_MemberRole;
    ALTER TABLE dbo.ProjectMembers DROP CONSTRAINT CK_ProjectRole;
    ALTER TABLE dbo.Members ADD CONSTRAINT FK_Member_Role FOREIGN KEY(Role) REFERENCES dbo.Roles(Name);
    ALTER TABLE dbo.ProjectMembers ADD CONSTRAINT FK_ProjectMember_Role FOREIGN KEY(Role) REFERENCES dbo.Roles(Name);
    INSERT dbo.SchemaVersions(Version) VALUES(4);
END;
COMMIT;
