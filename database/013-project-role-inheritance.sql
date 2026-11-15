SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE Version=13)
BEGIN
    ALTER TABLE dbo.Projects ADD InheritParentRoles bit NOT NULL
        CONSTRAINT DF_Projects_InheritParentRoles DEFAULT (0);

    EXEC(N'CREATE FUNCTION dbo.EffectiveProjectRoles(@projectId int, @userId int)
    RETURNS TABLE AS RETURN
    (
        WITH Ancestors AS (
            SELECT Id,ParentId,InheritParentRoles FROM dbo.Projects WHERE Id=@projectId
            UNION ALL
            SELECT p.Id,p.ParentId,p.InheritParentRoles FROM dbo.Projects p
            JOIN Ancestors a ON p.Id=a.ParentId AND a.InheritParentRoles=1
        )
        SELECT m.Role AS RoleName FROM dbo.ProjectMembers m
        JOIN Ancestors a ON a.Id=m.ProjectId WHERE m.UserId=@userId
        UNION
        SELECT r.RoleName FROM dbo.ProjectMemberRoles r
        JOIN Ancestors a ON a.Id=r.ProjectId WHERE r.UserId=@userId
    )');
    INSERT dbo.SchemaVersions(Version) VALUES (13);
END;
COMMIT;
