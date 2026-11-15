SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE Version=34)
BEGIN
    CREATE TABLE dbo.RegistrationRequests (
        Id int IDENTITY PRIMARY KEY,
        EmployeeNumber nvarchar(30) NOT NULL UNIQUE,
        DisplayName nvarchar(100) NOT NULL,
        PasswordHash nvarchar(500) NOT NULL,
        CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
    INSERT dbo.SchemaVersions(Version) VALUES (34);
END;
COMMIT;
