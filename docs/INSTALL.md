# Instalacija Team Managera

Status: razvojno izdanje. Aktualni implementirani opseg i preostali moduli zapisani su u docs/BACKLOG.md. Podržana baza ovog izdanja je SQL Server. Oracle/Db2/OCP nisu još implementirani.

## Iz izvornog koda

1. Instalirajte Git, .NET 10 SDK i SQL Server (lokalno može Express).
2. Klonirajte repozitorij i pokrenite `dotnet build src/TeamManager.Web`.
3. Iz korijena pokrenite `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Publish-Windows.ps1`.
4. ZIP u `artifacts` sadrži aplikaciju, SQL skripte, instalacijsku skriptu i primjer konfiguracije. Paket win-x64 uključuje .NET runtime.

## Konfiguracija

Raspakirajte paket u trajnu mapu aplikacije. Kopirajte `appsettings.Local.example.json` u `appsettings.Local.json`. Unesite SQL server, naziv baze i DNS naziv aplikacije u AllowedHosts.

Alternativa je varijabla okoline `ConnectionStrings__TeamManager`. Varijable imaju prednost nad JSON postavkama. Lozinke se ne spremaju u GitHub. Windows Integrated Security koristi identitet procesa aplikacije; taj identitet mora imati pristup bazi. SQL korisnik aplikacije treba SELECT/INSERT/UPDATE/DELETE nad aplikacijskim tablicama; instalaciju/nadogradnju izvršite zasebnim identitetom s DDL pravima.

Produkcijska veza provjerava SQL certifikat (`Encrypt=True;TrustServerCertificate=False`). Za lokalni SQL Express razvoj može se koristiti TrustServerCertificate=True. Certifikat banke i servername moraju odgovarati.

## Instalacija i nadogradnja baze

Iz mape paketa:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Install-Database.ps1 -CreateDatabase
```

Ako DBA unaprijed kreira praznu bazu, izostavite `-CreateDatabase`. Nadogradnja koristi istu naredbu bez tog prekidača. Migracije vode verziju u SchemaVersions; ponovljeno izvršavanje ne prazni tablice. Instalacijske skripte nisu zamjena za backup prije nadogradnje.

Za neobavezne probne podatke dodajte `-Demo`. Demo se ne učitava pri običnom pokretanju aplikacije. Prva prazna instalacija dobiva admin/admin; prijava nudi neobveznu promjenu lozinke. Postojeći korisnici nisu izmijenjeni. Ne koristite demo podatke kao stvarne bankarske podatke.

## Windows / IIS

Službena referenca: https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/?view=aspnetcore-10.0

1. Instalirajte IIS i službeni ASP.NET Core 10 Hosting Bundle. Self-contained paket i dalje zahtijeva ASP.NET Core Module za IIS.
2. Kreirajte application pool, No Managed Code, s namjenskim Windows identitetom.
3. Dodijelite čitanje mape aplikacije tom identitetu i potrebne SQL ovlasti.
4. U IIS-u kreirajte site s fizičkom putanjom paketa i HTTPS bindingom na certifikat za DNS naziv iz AllowedHosts. Publish generira web.config.
5. Postavite ASPNETCORE_ENVIRONMENT=Production. Provjerite HTTPS, prijavu i prikaz podataka.

Lokalno pokretanje iz izvornog koda: `dotnet dev-certs https --trust`, ASPNETCORE_ENVIRONMENT=Development, konfigurirana veza na bazu, pa `dotnet run --project src/TeamManager.Web --urls https://localhost:7043`.

## Podaci i nadogradnja aplikacije

Baza je izvan mape aplikacije. Sačuvajte appsettings.Local.json i ASP.NET Data Protection ključeve identiteta procesa pri nadogradnji. Zaustavite application pool, napravite backup baze i postavki, primijenite migracije, zamijenite aplikacijske datoteke, vratite lokalnu konfiguraciju i pokrenite pool. Povratak na staru verziju baze radi se iz provjerenog backupa, ne brisanjem tablica.

IIS instalacija u banci, vanjski certifikati, AD/LDAP i opterećenje zahtijevaju provjeru u ciljnom okruženju.

## Zdravstvene provjere

HTTPS GET na `/health/live` vraća 200 dok aplikacija odgovara. `/health/ready` vraća 200 kada aplikacija može otvoriti SQL vezu i čitati stupce računa koje koristi za provjeru sesija. Ako taj upit ne uspije, endpoint vraća 503. Oba endpointa dostupna su bez korisničke prijave i vraćaju samo status.

## SQL Server backup i povratak nakon nadogradnje

1. Zaustavite pisanje u bazu i sačuvajte prethodni aplikacijski paket, konfiguraciju i Data Protection ključeve. DBA izrađuje puni backup u novu datoteku na putanji dostupnoj SQL Server servisu. Sljedeći primjer koristi zasebnu datoteku za jednokratni backup prije migracije.

   ```sql
   BACKUP DATABASE [TeamManager]
       TO DISK = N'D:\SQLBackups\TeamManager-pre-upgrade.bak'
       WITH COPY_ONLY, CHECKSUM;
   RESTORE VERIFYONLY
       FROM DISK = N'D:\SQLBackups\TeamManager-pre-upgrade.bak'
       WITH CHECKSUM;
   ```

2. Za probu obnove i povratak nakon neuspjele migracije obnovite kopiju pod novim imenom, s odvojenim fizičkim datotekama. Iz `RESTORE FILELISTONLY` preuzmite logičke nazive i navedite `MOVE` za svaku datoteku.

   ```sql
   RESTORE FILELISTONLY FROM DISK = N'D:\SQLBackups\TeamManager-pre-upgrade.bak';
   RESTORE DATABASE [TeamManager_Restored]
       FROM DISK = N'D:\SQLBackups\TeamManager-pre-upgrade.bak'
       WITH CHECKSUM, RECOVERY,
            MOVE N'TeamManager' TO N'D:\SQLData\TeamManager_Restored.mdf',
            MOVE N'TeamManager_log' TO N'D:\SQLData\TeamManager_Restored.ldf';
   DBCC CHECKDB ([TeamManager_Restored]) WITH NO_INFOMSGS;
   ```

3. Provjerite poznate podatke u obnovljenoj bazi i SQL ovlasti aplikacijskog identiteta. Za povratak pokrenite prethodni paket s prethodnom konfiguracijom i vezom na obnovljenu bazu. Prije otvaranja pristupa provjerite prijavu i prikaz projekta. Obnova punog backupa vraća stanje u trenutku kopije. Ako je nakon kopije bilo upisa, DBA treba utvrditi točku oporavka i provjeriti dostupne log backup kopije prije prebacivanja prometa.

Lokalna provjera na odvojenim SQL Express bazama obuhvatila je backup s kontrolnom sumom, obnovu pod drugim imenom, DBCC CHECKDB i potvrdu da su obnovljeni projekti i korisnički podaci iz trenutka kopije.

Za ponovljivu probu nadogradnje i povratka pokrenite `pwsh -File scripts/Test-Rollback.ps1 -PreviousPackage <stara-mapa> -CurrentPackage <nova-mapa>`. Potreban je pouzdan razvojni HTTPS certifikat. Skripta koristi privremenu SQL Express bazu, kopiju s kontrolnom sumom i lokalni HTTPS port 7545. Provjerava prijavu i prikaz testnog projekta u oba paketa, a zatim u prethodnom paketu na obnovljenoj bazi. Za automatsko brisanje kopije po potrebi navedite `-BackupDirectory` s apsolutnom putanjom u koju SQL Server može pisati, a korisnik skripte može brisati vlastitu testnu datoteku. Ako brisanje ne uspije, skripta ispisuje putanju za čišćenje. IIS nadzor i povratak application poola provjeravaju se na IIS-u.

## Nadzor i logovi

Vanjski nadzor neka provjerava `/health/live` i `/health/ready` putem HTTPS-a. Odgovor 503 na readiness znači da aplikacija odgovara, ali SQL provjera nije prošla. Ako ni liveness ne odgovara, provjerite proces, IIS i mrežnu vezu.

Dok aplikacija radi, pozadinska SQL provjera bilježi početno stanje i svaku promjenu stanja pod kategorijom `TeamManager.Web.DatabaseHealthPublisher`. Oporavak ima razinu `Information`, a neuspjeh `Warning`. Upozorenja i oporavak prikupljajte preko konfiguriranog ASP.NET Core pružatelja logova. Ako na Windowsu koristite Event Log, za ovu kategoriju postavite `Logging__EventLog__LogLevel__TeamManager.Web.DatabaseHealthPublisher` na `Information` kako biste zabilježili i oporavak. Vanjski nadzor ostaje mjerodavan kad je proces ugašen ili se ne može pokrenuti.

## Jezik sučelja i proširenja projektne navigacije

Nakon migracije `031-account-language.sql` korisnik pod **Moj profil** može odabrati hrvatski ili engleski. Odabir se sprema po računu. Prijevod pokriva navigaciju, stranice implementiranih modula i poruke provjere obrazaca. Prije prijave koristi se podržani jezik iz postavki preglednika, uz hrvatski kao zadani jezik. Sadržaj projekata i vrijednosti šifrarnika prikazuju se kako su spremljeni. Prevedeni tekstovi koriste ASP.NET Core lokalizaciju i resurse `UiText.en.resx`.

Projektne poveznice mogu se dodati u `appsettings.Local.json` pod `ProjectExtensions:Links`. Primjer konfiguracije:

```json
{
  "ProjectExtensions": {
    "Links": [
      {
        "Label": "Priručnik",
        "EnglishLabel": "Handbook",
        "Url": "https://example.com/handbook",
        "ProjectId": "12"
      }
    ]
  }
}
```

Izostavite `ProjectId` za prikaz poveznice u svim dostupnim projektima. Konfigurirane poveznice prihvaćaju apsolutne HTTPS adrese. Proširenja iz izvornog koda registriraju se u DI spremniku kao implementacije `IProjectNavigationExtension`. Za dublje integracije predviđene su zasebne stavke backloga.

## Registracija uz odobrenje

Nakon migracije `034-registration-approval.sql` postavite `Registration__Enabled=true` ili `Registration:Enabled` na `true` u konfiguraciji aplikacije kako biste ponudili zahtjev za registraciju na stranici za prijavu. Zadano je isključena. Zahtjev sadrži broj radnika, ime i sažetak lozinke. Prijava je moguća tek nakon odobrenja.

Puni administrator otvara **Korisnici → Zahtjevi za registraciju**. Prije odobrenja provjerite broj radnika i potvrdite s radnikom da je osobno poslao zahtjev. Odobreni račun dobiva redovan pristup, a uloge se dodjeljuju zasebno. Odbijanje uklanja zahtjev. Odluke se bilježe u revizijskom zapisu. Registracija prihvaća do 10 POST zahtjeva po udaljenoj IP adresi u pet minuta. Iza proxyja ograničenje koristi IP adresu koju aplikacija vidi.

## Pregled Git commitova i razlika

Povežite repozitorij na stranici **Git repozitoriji**, zatim dodajte njegov ID iz poveznice za pregled ili iz tablice `ProjectRepositories` u lokalnu konfiguraciju aplikacije. Primjer za repozitorij s ID-jem 1:

```json
{
  "RepositoryHistory": {
    "Paths": {
      "1": "C:/Git/Team-Manager"
    }
  }
}
```

Identitet aplikacije treba moći pokrenuti `git` i čitati konfiguriranu radnu kopiju. Prikaz koristi posljednjih 40 commitova s lokalnog `HEAD`-a i tekstualni diff prema prvom roditelju. Veliki diffovi ograničeni su na 128 Ki znakova. Aplikacija koristi lokalne Git operacije čitanja. Sinkronizacija s udaljenim repozitorijem vodi se izvan aplikacije. Omogućite ovlast `repositories.view` samo korisnicima kojima želite dopustiti pregled izvornog koda i koji imaju pravo pregleda projekta. Puna administracija uključuje tu ovlast.

## SMTP obavijesti i podsjetnici

Primijenite migraciju `040-smtp-delivery.sql`. Šalje se samo kada je `Smtp:Enabled` uključen i aplikacija ima valjanu SMTP konfiguraciju. Primjer postavki za poslužitelj:

```json
{
  "Smtp": {
    "Enabled": true,
    "Host": "smtp.example.com",
    "Port": 587,
    "UseTls": true,
    "From": "team-manager@example.com",
    "BaseUrl": "https://team-manager.example.com",
    "ReminderTimeZone": "Europe/Zagreb"
  }
}
```

Ako poslužitelj traži prijavu, postavite `Smtp__Username` i `Smtp__Password` kroz konfiguraciju tajni okruženja. Pod **Moj profil → Obavijesti e-poštom** korisnici zasebno uključuju obavijesti za praćene zadatke i podsjetnike za otvorene zadatke dodijeljene njima na dan roka. Vremenska zona podsjetnika zadano je UTC. Poruka koristi jezik računa i sadrži poveznicu za pregled nakon prijave.

Obrada ponovno provjerava pristup i postavke primatelja. Nakon neuspjeha slanja red ponavlja pokušaj uz rastući odmak i zaključuje zapis nakon pet neuspjelih pokušaja. Podsjetnik se priprema jednom po zadatku, nositelju i datumu roka. Ako proces stane nakon što SMTP prihvati poruku i prije nego što SQL zabilježi završetak, ponovni pokušaj može poslati drugu kopiju.

## Ulazna e-pošta

Nakon migracije `041-incoming-issue-mail.sql` upravitelj projekta može otvoriti **Ulazna e-pošta** i provjeriti status i adresu za primitak poruka. Za projekt ID 3 postavite `MailIntake__Routes__3__EmployeeNumber` na namjenski aktivni račun s ovlastima pregleda i kreiranja zadataka te `MailIntake__Routes__3__Secret` na odvojenu tajnu od 32 nasumična bajta u 64 heksadekadska znaka.

Pouzdani vanjski pristupnik provjerava pošiljatelja i politiku prihvaćanja poruka, dekodira obični tekst te šalje HTTPS zahtjev `POST /mail/intake/3` sa zaglavljima `Authorization: Bearer …` i `Content-Type: application/json`. Primjer tijela zahtjeva:

```json
{
  "messageId": "<example-id@example.com>",
  "subject": "Naslov zadatka",
  "text": "Obični tekst poruke."
}
```

Zadržite isti `messageId` pri ponavljanju isporuke. Prvi prihvat vraća `201` i novi ID. Ponovljena poruka vraća `200` s oznakom `duplicate`. Zaprimanje stvara privatni zadatak u ciljanom projektu pod konfiguriranim računom. Ulaz podržava predmet do 200 znakova, tekst do 4000 znakova i tijelo zahtjeva do 64 KiB. Ako koristite vanjski pristupnik, on vodi povezivanje sandučića, MIME dekodiranje i pretvorbu poruke u ovaj zahtjev.

### Izravni IMAP sandučić

Za lokalnu provjeru iz korijena repozitorija pokrenite `pwsh -NoProfile -File scripts/Test-Imap.ps1 -Preview`. Potrebni su lokalni SQL Server Express (`localhost\SQLEXPRESS`) i pouzdani razvojni HTTPS certifikat (`dotnet dev-certs https --trust`). Skripta podiže lokalni TLS IMAP simulator s jednom testnom porukom i pokreće aplikaciju na zasebnoj privremenoj bazi.

Na [stranici Ulazna e-pošta](https://localhost:7544/MailIntake?projectId=1) prijavite se s `admin / admin` i otvorite posljednji uvezeni zadatak. Pritisnite Enter u terminalu za gašenje pregleda i brisanje privremene baze. Bez opcije `-Preview` skripta provjerava rezultat i sama završava.

Umjesto vanjskog pristupnika možete povezati namjensku IMAP mapu s projektom. Za lokalni demo projekt ID 3 postavite vrijednosti pod `MailIntake:Imap:3` u lokalnoj konfiguraciji. `Host` je naziv IMAP poslužitelja, `Port` zadano 993, `Folder` je puna putanja namjenske mape, a `EmployeeNumber` je aktivan Team Manager račun s ovlastima pregleda i kreiranja zadataka. Postavite `MailIntake__Imap__3__Username` i `MailIntake__Imap__3__Password` kroz lokalno okruženje ili odgovarajući spremnik tajni. Postavite `Enabled` na `true` kad je mapa spremna.

Konektor koristi IMAP preko TLS-a i traži korisničko ime i lozinku aplikacije. U mapu usmjerite samo poruke koje smiju otvarati zadatke. Prihvaćaju se nepročitane MIME poruke do 1 MiB s naslovom do 200 znakova i običnim tekstom do 4000 znakova. Nakon uspješnog uvoza ili prepoznavanja ponovljene poruke, konektor označuje poruku pročitanom. Poruke koje ne ispunjavaju uvjete ostaju nepročitane. U **Ulaznoj e-pošti** upravitelj vidi vrijeme zadnje provjere i poveznicu na posljednji uvezeni zadatak ako mu je dostupan.
