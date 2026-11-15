SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE Version=29)
BEGIN
    CREATE TABLE dbo.ForumTopics (
        Id int IDENTITY PRIMARY KEY,
        ProjectId int NOT NULL REFERENCES dbo.Projects(Id),
        AuthorId int NOT NULL REFERENCES dbo.AppUsers(Id),
        Title nvarchar(150) NOT NULL,
        Body nvarchar(4000) NOT NULL,
        IsLocked bit NOT NULL DEFAULT (0),
        CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_ForumTopics_Project ON dbo.ForumTopics(ProjectId,Id DESC);
    CREATE TABLE dbo.ForumReplies (
        Id int IDENTITY PRIMARY KEY,
        TopicId int NOT NULL REFERENCES dbo.ForumTopics(Id),
        AuthorId int NOT NULL REFERENCES dbo.AppUsers(Id),
        Body nvarchar(4000) NOT NULL,
        CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_ForumReplies_Topic ON dbo.ForumReplies(TopicId,Id);
    INSERT dbo.RolePermissions(RoleName,Permission)
    SELECT r.Name,p.Code FROM dbo.Roles r
    CROSS JOIN (VALUES('forum.post'),('forum.moderate')) p(Code)
    WHERE r.Name IN (N'Voditelj',N'Moderator')
       OR (p.Code='forum.post' AND r.Name IN (N'Developer',N'Član'));
    INSERT dbo.SchemaVersions(Version) VALUES (29);
END;
COMMIT;
