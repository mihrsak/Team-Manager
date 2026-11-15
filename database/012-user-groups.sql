SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE Version=12)
BEGIN
    CREATE TABLE dbo.UserGroups (
        Id int IDENTITY PRIMARY KEY,
        Name nvarchar(100) NOT NULL UNIQUE
    );
    CREATE TABLE dbo.UserGroupMembers (
        GroupId int NOT NULL REFERENCES dbo.UserGroups(Id),
        UserId int NOT NULL REFERENCES dbo.AppUsers(Id),
        PRIMARY KEY (GroupId, UserId)
    );
    CREATE INDEX IX_UserGroupMembers_UserId ON dbo.UserGroupMembers(UserId, GroupId);
    CREATE TABLE dbo.UserGroupRoles (
        GroupId int NOT NULL REFERENCES dbo.UserGroups(Id),
        RoleName nvarchar(30) NOT NULL REFERENCES dbo.Roles(Name),
        PRIMARY KEY (GroupId, RoleName)
    );
    EXEC(N'CREATE VIEW dbo.EffectiveUserRoles AS
        SELECT UserId,RoleName FROM dbo.UserRoles
        UNION
        SELECT m.UserId,r.RoleName FROM dbo.UserGroupMembers m
        JOIN dbo.UserGroupRoles r ON r.GroupId=m.GroupId');
    INSERT dbo.SchemaVersions(Version) VALUES (12);
END;
COMMIT;
