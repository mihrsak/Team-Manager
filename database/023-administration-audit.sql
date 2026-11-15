SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE Version=23)
BEGIN
    CREATE TABLE dbo.AdministrationAudit (
        Id bigint IDENTITY PRIMARY KEY,
        ActorId int NOT NULL,
        ActorEmployeeNumber nvarchar(30) NOT NULL,
        Action varchar(40) NOT NULL,
        Subject nvarchar(100) NOT NULL,
        CreatedAt datetime2 NOT NULL DEFAULT SYSUTCDATETIME()
    );
    INSERT dbo.SchemaVersions(Version) VALUES (23);
END;
COMMIT;
