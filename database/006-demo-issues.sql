-- Explicit optional demo only. Safe to run again; existing demo issues are not reset.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF NOT EXISTS(SELECT 1 FROM dbo.Projects WHERE Name=N'Demo • Team Manager')
BEGIN
    DECLARE @UserId int=(SELECT TOP(1) u.UserId FROM dbo.UserRoles u JOIN dbo.RolePermissions p ON p.RoleName=u.RoleName WHERE p.Permission='*' ORDER BY u.UserId);
    IF @UserId IS NULL THROW 51002,'Start application once to prepare the initial administrator before loading demo data.',1;
    INSERT dbo.Projects(Name,Description) VALUES(N'Demo • Team Manager',N'Demonstracijski projekt za zadatke, role, tijek rada i postavke projekta.');
    DECLARE @ProjectId int=SCOPE_IDENTITY();
    INSERT dbo.ProjectMembers(ProjectId,UserId,Role) VALUES(@ProjectId,@UserId,N'Voditelj');
    INSERT dbo.Issues(ProjectId,Title,Description,Type,Priority,Status,AssigneeId,DueDate,CreatedBy)
    VALUES(@ProjectId,N'Pripremiti razvojnu okolinu',N'Provjeriti SQL Server, HTTPS i instalacijske skripte.',N'Zadatak',N'Visok',N'Završeno',@UserId,CONVERT(date,GETDATE()),@UserId),
    (@ProjectId,N'Pregledati prava članova projekta',N'Isprobati role Developer, Moderator i Promatrač.',N'Zahtjev',N'Normalan',N'U tijeku',@UserId,DATEADD(day,3,CONVERT(date,GETDATE())),@UserId),
    (@ProjectId,N'Testirati rokove i komentare',N'Dodati komentar i evidentirati utrošeno vrijeme.',N'Zadatak',N'Normalan',N'Novo',@UserId,DATEADD(day,7,CONVERT(date,GETDATE())),@UserId);
    INSERT dbo.IssueEvents(IssueId,UserId,Kind,Body) SELECT Id,@UserId,'created',N'Demo zadatak kreiran.' FROM dbo.Issues WHERE ProjectId=@ProjectId;
END;

DECLARE @DemoStatuses TABLE(Name nvarchar(30) PRIMARY KEY, IsClosed bit, Description nvarchar(4000));
INSERT @DemoStatuses VALUES
(N'📋 Nije započeto',0,N'Zadatak je planiran, ali još nitko nije počeo raditi na njemu.'),
(N'🧠 U planiranju',0,N'Još se definiraju detalji – opseg, rokovi i resursi.'),
(N'⏳ U tijeku',0,N'Zadatak se trenutno aktivno radi.'),
(N'🕓 Na čekanju',0,N'Privremeno pauziran – čeka vanjski input ili rješenje drugog zadatka.'),
(N'❌ Blokirano',0,N'Ne može se izvršiti zbog problema, primjerice greške ili nedostupnog resursa.'),
(N'🔁 U reviziji / testiranju',0,N'Gotov, ali čeka provjeru, testiranje ili povratnu informaciju.'),
(N'✅ Završen',1,N'Zadatak je u potpunosti dovršen i odobren.'),
(N'🗃 Arhivirano',1,N'Star zadatak koji više nije aktivan, ali ostaje u sustavu.'),
(N'🚫 Otkazano',1,N'Zadatak je trajno uklonjen iz plana.'),
(N'📞 U podršci',0,N'Ad-hoc podrška ili postupanje po incidentu.'),
(N'♾️ Kontinuirano',0,N'Projekt je stalno aktivan, bez prekida.'),
(N'🔄 Neprekidno',0,N'Rad na projektu je stalan i odvija se u ciklusu.'),
(N'🔧 Održavanje / Support',0,N'Projekt je u režimu održavanja i podrške.'),
(N'📝 Nominacija projekta',0,N'Projekt je predložen ili kandidiran za razmatranje.'),
(N'⛔ Odbijen',1,N'Nominacija nije prošla odobrenje.'),
(N'🧾 UAT',0,N'Korisnički test je prošao.');

INSERT dbo.IssueStatuses(Name,IsClosed)
SELECT s.Name,s.IsClosed FROM @DemoStatuses s
WHERE NOT EXISTS(SELECT 1 FROM dbo.IssueStatuses existing WHERE existing.Name=s.Name);

IF NOT EXISTS(SELECT 1 FROM dbo.Projects WHERE Name=N'Demo • Statusi zadataka')
BEGIN
    DECLARE @StatusDemoUserId int=(SELECT TOP(1) u.UserId FROM dbo.UserRoles u JOIN dbo.RolePermissions p ON p.RoleName=u.RoleName WHERE p.Permission='*' ORDER BY u.UserId);
    IF @StatusDemoUserId IS NULL THROW 51002,'Start application once to prepare the initial administrator before loading demo data.',1;
    INSERT dbo.Projects(Name,Description) VALUES(N'Demo • Statusi zadataka',N'Po jedan testni zadatak za svaki demo status.');
    DECLARE @StatusDemoProjectId int=SCOPE_IDENTITY();
    INSERT dbo.ProjectMembers(ProjectId,UserId,Role) VALUES(@StatusDemoProjectId,@StatusDemoUserId,N'Voditelj');
    INSERT dbo.Issues(ProjectId,Title,Description,Type,Priority,Status,AssigneeId,CreatedBy)
    SELECT @StatusDemoProjectId,N'Demo · '+Name,Description,N'Zadatak',N'Normalan',Name,@StatusDemoUserId,@StatusDemoUserId
    FROM @DemoStatuses;
    INSERT dbo.IssueEvents(IssueId,UserId,Kind,Body)
    SELECT Id,@StatusDemoUserId,'created',N'Demo zadatak kreiran.' FROM dbo.Issues WHERE ProjectId=@StatusDemoProjectId;
END;
COMMIT;
