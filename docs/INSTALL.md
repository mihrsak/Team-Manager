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

## OpenID Connect SSO

SSO je zadano isključen. Za uključivanje registrirajte povjerljiv OIDC klijent s authorization code tokom i PKCE S256. U pružatelju identiteta konfigurirajte točan HTTPS povratni URL aplikacije sa završetkom `/signin-sso`.

Primjer dijela lokalne konfiguracije:

```json
{
  "Sso": {
    "Enabled": true,
    "Authority": "https://localhost:7563/realms/team-manager-qa",
    "ClientId": "team-manager",
    "Accounts": [
      { "Subject": "OIDC-sub-iz-pouzdanog-izdavatelja", "EmployeeNumber": "sso-demo" }
    ]
  }
}
```

Klijentsku tajnu postavite kroz `Sso__ClientSecret` ili lokalni spremnik tajni. `Authority` mora odgovarati izdavatelju (`issuer`) iz OIDC metapodataka. Za svaki postojeći račun koji koristi SSO povežite točnu vrijednost `sub` s brojem radnika. Ime, e-pošta i role iz tokena ne određuju aplikacijske ovlasti. Promjene lokalne konfiguracije primijenite ponovnim pokretanjem aplikacije. Gumb za odjavu zatvara Team Manager sesiju, a sesiju kod pružatelja identiteta uređuje njegova politika.

Lokalna proba koristi Keycloak na HTTPS portu 7563 i Team Manager na [portu 7564](https://localhost:7564/Login) s povratnim URL-om `https://localhost:7564/signin-sso`. Za ponoviti test `node scripts/Test-Sso.cjs`, koristite izdvojenu SQL bazu s početnim računom `admin / admin`, Node.js i Edge priključen kao u QA-02 uputama. Testni realm `team-manager-qa` ima korisnike `sso-demo`, `sso-unlinked` i `sso-inactive` s lokalnom testnom lozinkom `LocalSsoDemo!7563`. Povežite subjekte za prvi i treći račun. Skripta priprema njihove lokalne račune i deaktivira treći račun prije provjere.

## Windows / IIS

Službena referenca: https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/?view=aspnetcore-10.0

1. Instalirajte IIS i službeni ASP.NET Core 10 Hosting Bundle. Self-contained paket i dalje zahtijeva ASP.NET Core Module za IIS.
2. Kreirajte application pool, No Managed Code, s namjenskim Windows identitetom.
3. Dodijelite čitanje mape aplikacije tom identitetu i potrebne SQL ovlasti.
4. U IIS-u kreirajte site s fizičkom putanjom paketa i HTTPS bindingom na certifikat za DNS naziv iz AllowedHosts. Publish generira web.config.
5. Postavite ASPNETCORE_ENVIRONMENT=Production. Provjerite HTTPS, prijavu i prikaz podataka.

Lokalno pokretanje iz izvornog koda: `dotnet dev-certs https --trust`, ASPNETCORE_ENVIRONMENT=Development, konfigurirana veza na bazu, pa `dotnet run --project src/TeamManager.Web --urls https://localhost:7043`.

### Proba na lokalnom IIS-u

Na testnoj radnoj stanici s IIS-om, ASP.NET Core 10 Hosting Bundleom, SQL Server Expressom i alatom `sqlcmd` pokrenite iz korijena repozitorija u administratorskom **Windows PowerShellu**:

```powershell
.\scripts\Install-LocalIisTest.ps1 -PreviousPackage '<stara-mapa>' -CurrentPackage '<nova-mapa>'
```

Skripta postavlja site `TeamManager-LocalTest`, namjenski application pool i bazu `TeamManager_IisTest`. Podržava HTTPS preko IPv4 i IPv6 loopback adresa i dodaje namjenski razvojni certifikat u spremnik pouzdanih certifikata trenutnog korisnika. SQL račun application poola ima pravo čitanja i pisanja u testnoj bazi, a migracije izvršava korisnik skripte. Skripta odbija prepisati postojeću instalaciju s istim imenom.

Nakon provjere prijave, zdravstvenih provjera, nadogradnje i povratka, skripta ostavlja aktualni paket na [lokalnoj IIS adresi](https://localhost:7443/). Prijava `admin / admin` i projekt **IIS rollback test project** služe samo toj izdvojenoj bazi. Kopija baze prije nadogradnje ostaje u SQL Serverovoj mapi za sigurnosne kopije.

## Podaci i nadogradnja aplikacije

Baza je izvan mape aplikacije. Sačuvajte appsettings.Local.json i ASP.NET Data Protection ključeve identiteta procesa pri nadogradnji. Zaustavite application pool, napravite backup baze i postavki, primijenite migracije, zamijenite aplikacijske datoteke, vratite lokalnu konfiguraciju i pokrenite pool. Povratak na staru verziju baze radi se iz provjerenog backupa, ne brisanjem tablica.

IIS instalacija u banci, vanjski certifikati, AD/LDAP i opterećenje zahtijevaju provjeru u ciljnom okruženju.

## Integracijske provjere QA-01

Opcionalno učitavanje demo podataka (`--seed-demo` odnosno `Install-Database.ps1 -Demo`) dodaje projekt **Demo • Statusi zadataka** s jednim primjerom za svaki od 16 statusa. **Završen**, **Arhivirano**, **Otkazano** i **Odbijen** označeni su kao zatvoreni, a **UAT** je aktivan do završne odluke. Ponovno učitavanje čuva postojeće demo zadatke i statuse.

Demo učitavanje dodaje i projekte **Demo • Prioriteti zadataka** i **Demo • Tipovi zadataka** s primjerima za svih 8 prioriteta i 53 tipa. Broj prioriteta naveden je u naslovu primjera, a kategorija tipa u opisu. Ažuriranje sheme na verziju 42 podržava nazive tipova do 50 znakova. Demo vrijednosti učitavaju se samo pri izričitom pokretanju demo učitavanja.

Iz korijena repozitorija pokrenite `pwsh -NoProfile -File scripts/Test-Integration.ps1`. Potrebni su PowerShell 7, .NET SDK, Git, SQL Express (`localhost\SQLEXPRESS`), `sqlcmd` i pouzdan razvojni HTTPS certifikat (`dotnet dev-certs https --trust`).

Skripta objavljuje aktualni Release build i pokreće provjere bootstrapa, ovlasti, modula, Git povijesti, API-ja te ulazne i izlazne pošte u zasebnim privremenim SQL bazama. Koristi portove 7343, 7543, 7544 i 7643. Za test modula po potrebi odaberite drugi port parametrom `-IssuePort`. SMTP i IMAP provjere koriste lokalne simulatore. IIS testni site na portu 7443 može ostati pokrenut.

## Pregledničke provjere QA-02

`node scripts/Test-Browser.cjs` pokreće automatsku provjeru u Microsoft Edgeu. Potrebni su Node.js, Edge te paketi `playwright-core` 1.56.1 raspakiran u `artifacts/qa-browser/playwright` i `axe-core` 4.11.1 raspakiran u `artifacts/qa-browser/axe`. Raspakirajte sadržaj korijenske mape paketa (`package/`) izravno u odgovarajuću ciljnu mapu.

Pokrenite aplikaciju na `https://localhost:7560` s pouzdanim razvojnim certifikatom i zasebnom, svježe inicijaliziranom SQL bazom (`--apply-schema --create-database --seed-demo`). Skripta koristi početni račun `admin / admin` i demo projekt ID 1. Stvara testne korisnike, zadatke, komentare i zapise vremena. Kod ponavljanja koristite zasebnu QA bazu jer testni zapisi ostaju u njoj. Rezultati axe provjere pokrivaju automatizirana WCAG A/AA pravila na pregledanim stranicama.

Za isti preglednički test na lokalnom IIS testnom siteu postavite `$env:TEAM_MANAGER_QA_URL='https://localhost:7443'`. Lokalni test opterećenja pokrenite naredbom `node scripts/Test-IisLoad.cjs`. Ako Node ne prepoznaje lokalni certifikat, postavite `NODE_EXTRA_CA_CERTS` na putanju javnog PEM certifikata tog sitea. Skripta provjerava zdravstvene endpointove, zaštitu prijave i CSRF prije mjerenja te mjeri šest ruta za čitanje tijekom 10 sekundi po razini (1, 10 i 25 paralelnih zahtjeva). Rezultat ispisuje broj odgovora, zahtjeve u sekundi, medijan, p95 i greške.

Ručni HR/EN pregled obuhvaća redoslijed fokusa od prečaca do prijave, lokalizirane oznake i pogreške te čitljivost glavnih stranica na širini od 320 px. Pristupačna imena provjeravaju se i u stablu pristupačnosti preglednika.

Na lokalnom IIS-u provjeren je govorni izlaz NVDA 2026.3.1 s eSpeak NG u Microsoft Edgeu na oba jezika. Pokriveni su prečac na glavni sadržaj, polja prijave i poruka neuspjele prijave, navigacija do projekata, polja za izradu zadatka, komentar i vrijeme, pretraga i rezultat te odjava. Sam NVDA bio je postavljen na hrvatski pa su nazivi uloga kontrola poput „gumb” ostali na hrvatskom i uz engleske oznake aplikacije. Provjera kapaciteta na ciljnom produkcijskom IIS-u ostaje zasebna.

## OpenShift

Lokalna provjera slike provedena je naredbom `podman build -t localhost/team-manager:ops04-local .` i na zasebnoj bazi u SQL Server 2022 Express kontejneru. Migracije i web pokrenuti su iz iste slike s UID-om 10001, uz odvojene SQL račune. Lokalni HTTPS pregled objavljen je na [localhost:7550](https://localhost:7550/) dok aplikacijski i SQL kontejner rade. Provjereni su liveness, readiness, prijava, prikaz demo projekta i valjanost sesije nakon restarta uz trajni volumen za ključeve.

Izgradite Linux sliku iz korijena repozitorija (`docker build -t "$image" .`) i objavite je u registru koji ciljni OpenShift projekt može čitati. Koristite istu verziju slike za migraciju i web. Pripremite praznu SQL Server bazu pri prvoj instalaciji i dvije različite SQL veze u OpenShift Secret objektima:

- `team-manager-db`, ključ `connection-string`, za aplikacijski SQL račun s pravima čitanja i pisanja u bazi.
- `team-manager-migration`, ključ `connection-string`, za račun koji može primijeniti skripte sheme i inicijalizirati korisnika.
- `team-manager-bootstrap`, ključ `password`, za početni račun `admin` pri prvoj instalaciji. Lozinka mora imati najmanje 12 znakova. Nakon uspješne inicijalizacije Secret možete ukloniti. Instalacija na praznu bazu zahtijeva tajnu.

Za Route koristite DNS ime pokriveno certifikatom Ingress Controllera. Od administratora klastera preuzmite pouzdani CIDR neposrednih ingress proxyja kako ih vidi aplikacija. Taj CIDR određuje koje adrese smiju postaviti proslijeđenu shemu i klijentsku adresu. Predložak očekuje router u namespaceu `openshift-ingress` i upisiv RWO volumen iz zadane StorageClass za trajne ključeve sesije. Ako se nazivi u klasteru razlikuju, prilagodite NetworkPolicy odnosno PVC prije primjene. Pohranite ključeve na zaštićeni volumen.

Prije provjere na ciljnom klasteru pripremite:

- Namjenski testni projekt i korisnika ili servisni račun koji smije primijeniti predloške, pratiti Job i Deployment te čitati događaje i logove. Service Account aplikacije treba moći preuzeti sliku iz registra.
- Tag ili digest izgrađene Linux slike, registry pull secret ako je registar privatan i provjeru da arhitektura slike odgovara worker čvorovima.
- Testnu SQL Server bazu i prohodan, šifriran pristup iz podova. Pripremite aplikacijsku i migracijsku SQL vezu te, za praznu bazu, tajnu za inicijalizaciju administratora. Veza treba koristiti identitet dostupan iz Linux poda. Ako SQL koristi internu CA, osigurajte da joj slika vjeruje.
- DNS i TLS za Route, stvarni CIDR neposrednih ingress proxyja, odgovarajući ingress namespace i StorageClass čiji RWO volumen daje pravo pisanja UID-u koji dodjeljuje OpenShift.

Prije primjene provjerite oba obrađena predloška s `oc apply --dry-run=server -f -` u testnom projektu. Nakon instalacije provjerite i ostaje li prijava valjana nakon ponovnog pokretanja poda. Time provjeravate da novi pod može čitati ključeve sa zajedničkog PVC-a.

Iz administratorske konzole s odabranim ciljnim OpenShift projektom postavite `$image`, `$routeHost` i `$ingressCidr`, pa pokrenite:

```powershell
$job = 'team-manager-migrate-v1' # Novo ime za svaku nadogradnju.
oc process --local -f deployment/openshift/migrate.yaml -p "IMAGE=$image" -p "JOB_NAME=$job" | oc apply -f -
oc wait --for=condition=complete "job/$job" --timeout=5m
# Nastavite nakon uspješnog završetka Joba.
oc process --local -f deployment/openshift/app.yaml -p "IMAGE=$image" -p "ROUTE_HOST=$routeHost" -p "TRUSTED_INGRESS_CIDR=$ingressCidr" | oc apply -f -
oc rollout status deployment/team-manager
curl.exe --fail "https://$routeHost/health/ready"
```

Prije nadogradnje napravite sigurnosnu kopiju baze i skalirajte postojeći Deployment na nula replika dok se primjenjuju migracije. Nakon rollouta provjerite HTTP preusmjeravanje na HTTPS, prijavu, `/health/live` i `/health/ready`. Route završava TLS na ingressu i promet prema podu šalje preko HTTP-a. Pod koristi HTTP zdravstvene provjere s očekivanim `Host` zaglavljem.

### Lokalna provjera kroz HTTPS proxy

Uz Node.js, pouzdani razvojni certifikat (`dotnet dev-certs https --trust`) i zasebnu bazu `TeamManager_IisTest` iz lokalne IIS probe pokrenite `pwsh -NoProfile -File scripts/Test-ReverseProxy.ps1 -Preview`. Skripta podiže HTTPS proxy na [localhost:7547](https://localhost:7547/) i pozadinski HTTP na loopbacku. Provjerava prijavu u testni projekt, `Secure` cookie, zdravstvene provjere i da aplikacija zanemaruje proslijeđenu HTTPS oznaku s nepouzdane adrese. Bez `-Preview` oba se procesa po završetku zaustavljaju. Preview ispisuje PID-ove pokrenutih procesa za kasnije gašenje.

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
