-- Pokrenuti u odabranoj TeamManager bazi. Ne brise postojece podatke.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.SchemaVersions', N'U') IS NULL
    CREATE TABLE dbo.SchemaVersions (Version int NOT NULL PRIMARY KEY, AppliedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME());
IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE Version = 1)
BEGIN
    CREATE TABLE dbo.Teams (
        Id int IDENTITY PRIMARY KEY,
        Name nvarchar(100) NOT NULL UNIQUE,
        CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
    CREATE TABLE dbo.Members (
        Id int IDENTITY PRIMARY KEY,
        TeamId int NOT NULL REFERENCES dbo.Teams(Id),
        Name nvarchar(100) NOT NULL,
        CONSTRAINT UQ_Members_Team UNIQUE (TeamId, Id),
        CONSTRAINT UQ_Members_Name UNIQUE (TeamId, Name)
    );
    CREATE TABLE dbo.WorkItems (
        Id int IDENTITY PRIMARY KEY,
        TeamId int NOT NULL REFERENCES dbo.Teams(Id),
        AssigneeId int NULL,
        Title nvarchar(200) NOT NULL,
        Status nvarchar(20) NOT NULL DEFAULT N'Novo',
        DueDate date NULL,
        CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_WorkItems_Assignee FOREIGN KEY (TeamId, AssigneeId) REFERENCES dbo.Members(TeamId, Id),
        CONSTRAINT CK_WorkItems_Status CHECK (Status IN (N'Novo', N'U tijeku', N'Završeno'))
    );
    CREATE INDEX IX_WorkItems_TeamId ON dbo.WorkItems(TeamId);
    INSERT dbo.SchemaVersions(Version) VALUES (1);
END;
COMMIT;
