# Provjere

Preduvjeti: Windows PowerShell, .NET 10 SDK, lokalni SQL Server localhost\SQLEXPRESS s Windows prijavom i pouzdan ASP.NET razvojni certifikat.

```powershell
.\.tools\dotnet\dotnet.exe build src\TeamManager.Web -o artifacts\release-build
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Test-Bootstrap.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Test-Issues.ps1
```

Ako SDK nije u .tools, koristite `dotnet` za build; testni helperi trenutačno očekuju lokalni SDK u .tools. Testovi bootstrapa i zadataka kreiraju jedinstvenu privremenu SQL bazu, pokreću aplikaciju na localhost:7343/7443 i uklanjaju vlastite testne baze u finally bloku. Potrebne su ovlasti kreiranja/brisanja baza. Nemojte ih povezivati s produkcijskim SQL serverom.

Za provjeru administracije na pokrenutoj lokalnoj aplikaciji:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Test-Users.ps1 -AdminEmployee admin -AdminPassword admin
```

Navedite stvarni lokalni račun ako početni admin više nije u uporabi. Test kreira privremenog korisnika i rolu te ih uklanja; provjerava zabranu uklanjanja posljednjeg administratora. Pokreće se na razvojnoj bazi.

Pokriveno: bootstrap, opcionalna promjena lozinke, prijava, ovlasti, projektna izolacija, unos korisnika, promjena lozinke, migracije/demo ponovljivost, zadaci, konkurentne izmjene, komentari/XSS kodiranje, sati i CSRF.

Nije pokriveno ovim skriptama: bankarski IIS/AD/OCP, opterećenje, backup restore i puna preglednička automatizacija. Backlog vodi preostale provjere.

PROJ-02: Test-Issues sada ima 35 provjera. Provjerava i kružne veze u hijerarhiji, konkurentno uređivanje projekta, privatnost, javno čitanje bez prava pisanja, zatvaranje/arhiviranje/ponovno otvaranje, izolaciju voditelja projekta i očuvanje postavki nakon ponovljene instalacije.

PROJ-03: Test-Issues ima ukupno 51 provjeru. Novi scenariji provjeravaju ovlasti za module, skrivanje vremena i povijesti, zabranu izravnih GET/POST zahtjeva, konkurentne postavke, ponovljenu migraciju, ponovno uključivanje bez gubitka podataka i izolaciju projekata.
