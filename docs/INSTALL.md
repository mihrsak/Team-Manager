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

Lokalna provjera na odvojenim SQL Express bazama obuhvatila je backup s kontrolnom sumom, obnovu pod drugim imenom, DBCC CHECKDB i potvrdu da su obnovljeni projekti i korisnički podaci iz trenutka kopije. Povratak IIS paketa provjerava se u ciljnom okruženju.

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
