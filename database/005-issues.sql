SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF NOT EXISTS(SELECT 1 FROM dbo.SchemaVersions WHERE Version=5)
BEGIN
    CREATE TABLE dbo.Issues(
        Id int IDENTITY PRIMARY KEY, ProjectId int NOT NULL REFERENCES dbo.Projects(Id),
        Title nvarchar(200) NOT NULL, Description nvarchar(4000) NOT NULL DEFAULT N'',
        Type nvarchar(30) NOT NULL CHECK(Type IN(N'Zadatak',N'Greška',N'Zahtjev')),
        Priority nvarchar(20) NOT NULL CHECK(Priority IN(N'Nizak',N'Normalan',N'Visok',N'Hitno')),
        Status nvarchar(30) NOT NULL CHECK(Status IN(N'Novo',N'U tijeku',N'Na provjeri',N'Završeno')),
        AssigneeId int NULL REFERENCES dbo.AppUsers(Id), DueDate date NULL,
        CreatedBy int NOT NULL REFERENCES dbo.AppUsers(Id), CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
        Revision int NOT NULL DEFAULT 1
    );
    CREATE INDEX IX_Issues_ProjectStatus ON dbo.Issues(ProjectId,Status);
    CREATE TABLE dbo.IssueEvents(
        Id int IDENTITY PRIMARY KEY, IssueId int NOT NULL REFERENCES dbo.Issues(Id),
        UserId int NOT NULL REFERENCES dbo.AppUsers(Id), Kind varchar(20) NOT NULL,
        Body nvarchar(max) NOT NULL, CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
    CREATE TABLE dbo.TimeEntries(
        Id int IDENTITY PRIMARY KEY, IssueId int NOT NULL REFERENCES dbo.Issues(Id),
        UserId int NOT NULL REFERENCES dbo.AppUsers(Id), WorkDate date NOT NULL,
        Hours decimal(6,2) NOT NULL CHECK(Hours>0 AND Hours<=24), Note nvarchar(1000) NOT NULL,
        CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
    INSERT dbo.RolePermissions(RoleName,Permission)
    SELECT r.Name,p.Code FROM dbo.Roles r CROSS JOIN (VALUES('issues.view'),('issues.create'),('issues.edit'),('issues.comment'),('time.log'))p(Code)
    WHERE r.Name IN(N'Developer',N'Voditelj');
    INSERT dbo.RolePermissions VALUES(N'Član','issues.view'),(N'Član','issues.comment'),(N'Član','time.log'),(N'Promatrač','issues.view'),(N'Moderator','issues.view'),(N'Moderator','issues.comment');
    INSERT dbo.SchemaVersions(Version) VALUES(5);
END;
COMMIT;
