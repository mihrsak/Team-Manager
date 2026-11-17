-- Optional demo only. Keep existing project data and workflow configuration.
SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @Priorities TABLE(Number int NOT NULL, Name nvarchar(20) PRIMARY KEY);
INSERT @Priorities VALUES
    (1,N'🔴++ Kritično'),
    (2,N'🔺 Vrlo visok'),
    (3,N'🔶 Visok'),
    (4,N'🟡 Srednje'),
    (5,N'🔵 Nizak'),
    (6,N'⚪ Vrlo nizak'),
    (0,N'🚧 Blokirano'),
    (0,N'❌ Otkazano');

DECLARE @Types TABLE(Name nvarchar(50) PRIMARY KEY, Category nvarchar(100), Description nvarchar(4000));
INSERT @Types VALUES
    (N'📄 Tehnička dokumentacija',N'Dokumentacija',N'Pisanje ili ažuriranje specifikacija, API dokumenata itd.'),
    (N'🗃️ Funkcionalna dokumentacija',N'Dokumentacija',N'Opisi ponašanja sustava iz korisničke perspektive.'),
    (N'🧾 Release notes / changelog',N'Dokumentacija',N'Bilježenje promjena koje su isporučene.'),
    (N'📁 Upravljanje zadacima',N'Administrativni zadaci',N'Ažuriranje task boarda, Jira ticketa, prioriteta'),
    (N'📅 Upravljanje vremenom',N'Administrativni zadaci',N'Planiranje sprintova, rezervacija termina.'),
    (N'📖 Čitanje dokumentacije',N'Analiza i istraživanje',N'Detaljno proučavanje tehničke ili funkcionalne dokumentacije.'),
    (N'🔍 Istraživanje tehnologije',N'Analiza i istraživanje',N'Upoznavanje s novim alatima, bibliotekama ili rješenjima.'),
    (N'🧠 Analiza performansi',N'Analiza i istraživanje',N'Mjerenje i optimizacija brzine, memorije, skalabilnosti.'),
    (N'🏗️ Tehnički dug',N'Inicijative i unapređenja',N'Rješavanje zanemarenih tehničkih problema koji utječu na održavanje.'),
    (N'💡 Predlaganje poboljšanja',N'Inicijative i unapređenja',N'Aktivnosti usmjerene na poboljšanje procesa, alata ili proizvoda.'),
    (N'🔙 Rollback / Hotfix',N'Isporuka',N'Vraćanje na staru verziju ili brzo popravljanje problema u produkciji.'),
    (N'🗂️ Administracija / Operative',N'Isporuka',N'Održavanje sustava, pristupa, servera, računa, i operativni zadaci.'),
    (N'🚀 Deploy / Isporuka',N'Isporuka',N'Postavljanje verzije aplikacije na testno ili produkcijsko okruženje.'),
    (N'🧰 Konfiguracija alata / okruženja',N'Isporuka',N'Podesavanje CI/CD, servera, build sistema itd.'),
    (N'🧹 Priprema za produkciju',N'Isporuka',N'Završne pripreme prije puštanja promjena uživo.'),
    (N'🖍️ Prijedlog UX/UI poboljšanja',N'Korisničko iskustvo (UX)',N'Prijedlozi za poboljšanje korisničkog sučelja ili toka.'),
    (N'🧪 UX testiranje',N'Korisničko iskustvo (UX)',N'Provjera upotrebljivosti sučelja s korisnicima.'),
    (N'🔄 Redovno održavanje',N'Održavanje i podrška sustava',N'Backupi, patchiranja, monitoring.'),
    (N'🚨 Incident response',N'Održavanje i podrška sustava',N'Hitne intervencije kod problema u produkciji.'),
    (N'🧯 Rješavanje produkcijskih problema',N'Održavanje i podrška sustava',N'Dubinska analiza i sanacija problema na produkciji.'),
    (N'👥 Koordinacija tima',N'Organizacijsko-planerski',N'Organizacija rada i suradnje među članovima tima.'),
    (N'📊 Izvještavanje',N'Organizacijsko-planerski',N'Izrada i prezentacija izvještaja o statusu i napretku.'),
    (N'🔍 Revizija',N'Organizacijsko-planerski',N'Pregled postojećih rješenja, procedura ili procesa.'),
    (N'🗺️ Planiranje',N'Organizacijsko-planerski',N'Definiranje ciljeva, aktivnosti i vremenskih okvira.'),
    (N'🧭 Tehničko vodstvo',N'Organizacijsko-planerski',N'Smjer, savjeti i tehnička podrška timu na višoj razini.'),
    (N'🧭 Upravljanje projektom',N'Organizacijsko-planerski',N'Vođenje projekta, praćenje rokova i upravljanje timovima.'),
    (N'🧱 Arhitektura / Dizajn',N'Organizacijsko-planerski',N'Planiranje strukture sustava, modula i tehničkih rješenja'),
    (N'✅ Validacija',N'QA i validacija',N'Potvrda da sustav ispunjava funkcionalne zahtjeve.'),
    (N'📝 Test planiranje',N'QA i validacija',N'Izrada plana testiranja, strategije i kriterija.'),
    (N'🧠 Analiza zahtjeva',N'QA i validacija',N'Pregled i razrada korisničkih i poslovnih potreba.'),
    (N'🧪 QA (kontrola kvalitete)',N'QA i validacija',N'Procesi osiguravanja i praćenja kvalitete softvera.'),
    (N'🧾 Pisanje testnih slučajeva',N'QA i validacija',N'Definiranje konkretnih testova za pokrivanje funkcionalnosti.'),
    (N'🎓 Edukacija',N'Suradnja i podrška',N'Interna ili eksterna edukacija članova tima ili korisnika.'),
    (N'🎓 Razvoj tima / znanja',N'Suradnja i podrška',N'Aktivnosti za jačanje tima kroz edukaciju i mentorstvo.'),
    (N'🎧 Podrška korisnicima',N'Suradnja i podrška',N'Odgovaranje na upite, rješavanje prijavljenih problema.'),
    (N'💬 Retro / Feedback',N'Suradnja i podrška',N'Retrospektive i povratne informacije radi poboljšanja procesa.'),
    (N'🗣️ Sastanci',N'Suradnja i podrška',N'Formalni i neformalni radni sastanci, dnevni stand-up.'),
    (N'🤝 Komunikacija s klijentima',N'Suradnja i podrška',N'Interakcija s korisnicima, poslovnim analitičarima ili partnerima.'),
    (N'🧑‍🏫 Mentorski rad',N'Suradnja i podrška',N'Pružanje podrške i znanja manje iskusnim kolegama'),
    (N'🧪 POC (Proof of Concept)',N'Tehničke provjere / testne instalacije',N'Brza provjera je li nešto tehnički izvedivo.'),
    (N'🧫 Eksperimentalni setup',N'Tehničke provjere / testne instalacije',N'Postavljanje testne instance za provjeru ideje.'),
    (N'✅ Testiranje',N'Tehnički zadatak',N'Izvođenje testova za provjeru ispravnosti funkcionalnosti.'),
    (N'♻️ Refaktoring',N'Tehnički zadatak',N'Unaprjeđenje postojećeg koda bez promjene ponašanja.'),
    (N'🐞 Prijava/Bug',N'Tehnički zadatak',N'Identifikacija i evidentiranje grešaka u sustavu.'),
    (N'📊 Projektni zadatak',N'Tehnički zadatak',N'Kombinira planiranje, upravljanje, koordinaciju i analizu zahtjeva.'),
    (N'🔐 Sigurnosne provjere',N'Tehnički zadatak',N'Provjera sigurnosnih aspekata sustava (npr. autentikacija, autorizacija).'),
    (N'🔗 Integracija',N'Tehnički zadatak',N'Povezivanje sustava, modula ili servisa u jednu cjelinu.'),
    (N'🔧 Implementacija',N'Tehnički zadatak',N'Razvoj i kodiranje funkcionalnosti prema zahtjevima.'),
    (N'🛠️ Tehnički zadatak',N'Tehnički zadatak',N'Obuhvaća razvoj, testiranje, refaktoring, deploy i srodne tehničke aktivnosti.'),
    (N'🤖 Automatizacija',N'Tehnički zadatak',N'Izrada skripti, CI/CD, testova i procesa koji štede ručni rad.'),
    (N'🧩 Upravljanje bazom podataka',N'Tehnički zadatak',N'Rad s bazama podataka: migracije, optimizacije, izmjene shema.'),
    (N'📜 Usklađivanje s regulativama',N'Usklađenost i regulativa',N'GDPR, ISO, sigurnosni standardi.'),
    (N'📋 Interna pravila / procedure',N'Usklađenost i regulativa',N'Praćenje internih pravila, npr. kod review protokola.');

INSERT dbo.IssuePriorities(Name)
SELECT p.Name FROM @Priorities p
WHERE NOT EXISTS(SELECT 1 FROM dbo.IssuePriorities existing WHERE existing.Name=p.Name);

DECLARE @NewTypes TABLE(Name nvarchar(50) PRIMARY KEY);
INSERT dbo.IssueTypes(Name)
OUTPUT inserted.Name INTO @NewTypes
SELECT t.Name FROM @Types t
WHERE NOT EXISTS(SELECT 1 FROM dbo.IssueTypes existing WHERE existing.Name=t.Name);

INSERT dbo.WorkflowTransitions(RoleName,Type,FromStatus,ToStatus)
SELECT w.RoleName,t.Name,w.FromStatus,w.ToStatus
FROM dbo.WorkflowTransitions w CROSS JOIN @NewTypes t
WHERE w.Type=N'Zadatak' AND w.RoleName IN(N'Developer',N'Voditelj');

DECLARE @UserId int=(
    SELECT TOP(1) u.UserId FROM dbo.UserRoles u
    JOIN dbo.RolePermissions p ON p.RoleName=u.RoleName WHERE p.Permission='*' ORDER BY u.UserId
);
IF @UserId IS NULL THROW 51002,'Start application once to prepare the initial administrator before loading demo data.',1;

DECLARE @Projects TABLE(Id int, Name nvarchar(100));
INSERT dbo.Projects(Name,Description)
OUTPUT inserted.Id, inserted.Name INTO @Projects
SELECT demo.Name,demo.Description
FROM (VALUES
    (N'Demo • Prioriteti zadataka',N'Po jedan testni zadatak za svaki demo prioritet.'),
    (N'Demo • Tipovi zadataka',N'Po jedan testni zadatak za svaki demo tip.')
) demo(Name,Description)
WHERE NOT EXISTS(SELECT 1 FROM dbo.Projects existing WHERE existing.Name=demo.Name);

INSERT dbo.ProjectMembers(ProjectId,UserId,Role)
SELECT Id,@UserId,N'Voditelj' FROM @Projects;

DECLARE @Issues TABLE(Id int);
INSERT dbo.Issues(ProjectId,Title,Description,Type,Priority,Status,AssigneeId,CreatedBy)
OUTPUT inserted.Id INTO @Issues
SELECT p.Id,N'Demo · '+CONVERT(nvarchar(10),v.Number)+N' · '+v.Name,
       N'Prioritet iz demo šifrarnika.',N'Zadatak',v.Name,N'Novo',@UserId,@UserId
FROM @Projects p CROSS JOIN @Priorities v
WHERE p.Name=N'Demo • Prioriteti zadataka'
UNION ALL
SELECT p.Id,N'Demo · '+t.Name,t.Category+N': '+t.Description,t.Name,N'Normalan',N'Novo',@UserId,@UserId
FROM @Projects p CROSS JOIN @Types t
WHERE p.Name=N'Demo • Tipovi zadataka';

INSERT dbo.IssueEvents(IssueId,UserId,Kind,Body)
SELECT Id,@UserId,'created',N'Demo zadatak kreiran.' FROM @Issues;
COMMIT;
