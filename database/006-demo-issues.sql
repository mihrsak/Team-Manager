-- Explicit optional demo only. Safe to run again; existing demo issues are not reset.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF NOT EXISTS(SELECT 1 FROM dbo.Projects WHERE Name=N'Demo • Team Manager')
BEGIN
    DECLARE @UserId int=(SELECT TOP(1) u.UserId FROM dbo.UserRoles u JOIN dbo.RolePermissions p ON p.RoleName=u.RoleName WHERE p.Permission='*' ORDER BY u.UserId);
    IF @UserId IS NULL THROW 51002,'Start application once to prepare the initial administrator before loading demo data.',1;
    INSERT dbo.Projects(Name) VALUES(N'Demo • Team Manager');
    DECLARE @ProjectId int=SCOPE_IDENTITY();
    INSERT dbo.ProjectMembers(ProjectId,UserId,Role) VALUES(@ProjectId,@UserId,N'Voditelj');
    INSERT dbo.Issues(ProjectId,Title,Description,Type,Priority,Status,AssigneeId,DueDate,CreatedBy)
    VALUES(@ProjectId,N'Pripremiti razvojnu okolinu',N'Provjeriti SQL Server, HTTPS i instalacijske skripte.',N'Zadatak',N'Visok',N'Završeno',@UserId,CONVERT(date,GETDATE()),@UserId),
    (@ProjectId,N'Pregledati prava članova projekta',N'Isprobati role Developer, Moderator i Promatrač.',N'Zahtjev',N'Normalan',N'U tijeku',@UserId,DATEADD(day,3,CONVERT(date,GETDATE())),@UserId),
    (@ProjectId,N'Testirati rokove i komentare',N'Dodati komentar i evidentirati utrošeno vrijeme.',N'Zadatak',N'Normalan',N'Novo',@UserId,DATEADD(day,7,CONVERT(date,GETDATE())),@UserId);
    INSERT dbo.IssueEvents(IssueId,UserId,Kind,Body) SELECT Id,@UserId,'created',N'Demo zadatak kreiran.' FROM dbo.Issues WHERE ProjectId=@ProjectId;
END;
COMMIT;
