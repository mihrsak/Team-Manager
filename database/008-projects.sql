SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF NOT EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE Version=8)
BEGIN
    ALTER TABLE dbo.Projects ADD Description nvarchar(4000) NOT NULL CONSTRAINT DF_Project_Description DEFAULT N'',
        ParentId int NULL REFERENCES dbo.Projects(Id),
        IsPublic bit NOT NULL CONSTRAINT DF_Project_Public DEFAULT 0,
        State varchar(10) NOT NULL CONSTRAINT DF_Project_State DEFAULT 'Active',
        Revision int NOT NULL CONSTRAINT DF_Project_Revision DEFAULT 1;
    EXEC(N'ALTER TABLE dbo.Projects ADD CONSTRAINT CK_Project_State CHECK(State IN (''Active'',''Closed'',''Archived'')), CONSTRAINT CK_Project_Parent CHECK(ParentId<>Id)');
    IF EXISTS(SELECT 1 FROM dbo.Roles WHERE Name=N'Voditelj')
        INSERT dbo.RolePermissions(RoleName,Permission) VALUES(N'Voditelj','projects.manage');
    INSERT dbo.SchemaVersions(Version) VALUES(8);
END;
COMMIT;
