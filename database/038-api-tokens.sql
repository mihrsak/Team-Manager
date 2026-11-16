SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE Version=38)
BEGIN
    CREATE TABLE dbo.ApiTokens (
        Id int IDENTITY PRIMARY KEY,
        UserId int NOT NULL REFERENCES dbo.AppUsers(Id),
        Name nvarchar(80) NOT NULL,
        TokenHash binary(32) NOT NULL UNIQUE,
        SessionVersion int NOT NULL,
        CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME(),
        ExpiresAt datetime2 NOT NULL
    );
    CREATE INDEX IX_ApiTokens_User ON dbo.ApiTokens(UserId,Id);
    INSERT dbo.SchemaVersions(Version) VALUES (38);
END;
COMMIT;
