SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE Version=33)
BEGIN
    CREATE TABLE dbo.CustomFields (
        Id int IDENTITY PRIMARY KEY,
        Scope varchar(8) NOT NULL CHECK (Scope IN ('user','project','time')),
        Name nvarchar(100) NOT NULL,
        UNIQUE (Scope,Name),
        UNIQUE (Scope,Id)
    );
    CREATE TABLE dbo.UserFieldValues (
        EntityId int NOT NULL REFERENCES dbo.AppUsers(Id),
        FieldId int NOT NULL,
        Scope varchar(8) NOT NULL DEFAULT ('user') CHECK (Scope='user'),
        Value nvarchar(500) NULL,
        Revision int NOT NULL,
        PRIMARY KEY(EntityId,FieldId),
        FOREIGN KEY(Scope,FieldId) REFERENCES dbo.CustomFields(Scope,Id)
    );
    CREATE TABLE dbo.ProjectFieldValues (
        EntityId int NOT NULL REFERENCES dbo.Projects(Id),
        FieldId int NOT NULL,
        Scope varchar(8) NOT NULL DEFAULT ('project') CHECK (Scope='project'),
        Value nvarchar(500) NULL,
        Revision int NOT NULL,
        PRIMARY KEY(EntityId,FieldId),
        FOREIGN KEY(Scope,FieldId) REFERENCES dbo.CustomFields(Scope,Id)
    );
    CREATE TABLE dbo.TimeFieldValues (
        EntityId int NOT NULL REFERENCES dbo.TimeEntries(Id),
        FieldId int NOT NULL,
        Scope varchar(8) NOT NULL DEFAULT ('time') CHECK (Scope='time'),
        Value nvarchar(500) NULL,
        Revision int NOT NULL,
        PRIMARY KEY(EntityId,FieldId),
        FOREIGN KEY(Scope,FieldId) REFERENCES dbo.CustomFields(Scope,Id)
    );
    INSERT dbo.SchemaVersions(Version) VALUES (33);
END;
COMMIT;
