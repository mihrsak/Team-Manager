SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF NOT EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE Version=36)
BEGIN
    ALTER TABLE dbo.Issues ADD IsPrivate bit NOT NULL CONSTRAINT DF_Issues_IsPrivate DEFAULT (0);
    EXEC(N'CREATE FUNCTION dbo.IssueVisibility(
        @user int, @project int, @private bit, @creator int, @assignee int)
    RETURNS TABLE AS RETURN
    (
        SELECT 1 AS Allowed
        WHERE @private=0 OR @user=@creator OR @user=@assignee
           OR EXISTS (
               SELECT 1 FROM dbo.EffectiveUserRoles r JOIN dbo.RolePermissions p ON p.RoleName=r.RoleName
               WHERE r.UserId=@user AND p.Permission IN (''*'',''projects.manage'')
           )
           OR EXISTS (
               SELECT 1 FROM dbo.EffectiveProjectRoles(@project,@user) r JOIN dbo.RolePermissions p ON p.RoleName=r.RoleName
               WHERE p.Permission IN (''*'',''projects.manage'')
           )
    )');
    INSERT dbo.SchemaVersions(Version) VALUES (36);
END;
COMMIT;
