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

## Životni ciklus računa

Migracija `010-account-lifecycle.sql` postojeće račune zadržava aktivnima. Primijeni migraciju prije pokretanja nove verzije. Nakon nadogradnje postojeće sesije traže novu prijavu.

U popisu korisnika otvori **Profil** testnog korisnika. Puni administrator može deaktivirati i ponovno aktivirati račun, resetirati lozinku ili opozvati sesije. Reset uklanja privremeno zaključavanje računa. Sve četiri radnje poništavaju prethodne sesije pri sljedećem zahtjevu. Za vlastitu lozinku koristi redovnu promjenu lozinke na vlastitom profilu.

`Test-Users.ps1` pokriva ovlasti, prijavu deaktiviranog i ponovno aktiviranog korisnika, poništenje kolačića, te prihvaćanje nove i odbijanje stare lozinke nakon reseta.

## Više uloga po članstvu

Migracija `011-membership-roles.sql` zadržava postojeću ulogu kao osnovnu. Na profilu korisnika moguće je dodati ili ukloniti dodatnu ulogu te zamijeniti osnovnu. Prava svih dodijeljenih uloga zbrajaju se unutar pripadajućeg tima ili projekta. Uklanjanjem članstva uklanjaju se i njegove dodatne uloge.

## Grupe korisnika

Nakon migracije `012-user-groups.sql`, puni administrator može otvoriti **Role i ovlasti → Grupe korisnika**, kreirati grupu te dodavati i uklanjati članove i globalne uloge. Grupne uloge zbrajaju se s izravno dodijeljenim globalnim ulogama. Izmjene se primjenjuju pri sljedećem zahtjevu. Za zaštitu od gubitka administrativnog pristupa u aplikaciji mora ostati barem jedan aktivan korisnik s izravno dodijeljenom punom administracijom.

## Izvještaj o utrošenom vremenu

Migracija `014-time-entry-activity.sql` postojećim zapisima dodjeljuje aktivnost **Općenito**. Pri unosu vremena na zadatku može se navesti aktivnost do 60 znakova. U navigaciji **Izvještaj o vremenu** odaberite projekt, osobu, aktivnost ili datumski raspon. Izvještaj zbraja sate po projektu, osobi i aktivnosti te prikazuje sveukupne sate za odabrane filtre. Oba datuma uključena su u raspon.

Izvještaj prati postojeća pravila pregleda utrošenog vremena, uključujući javne projekte dostupne prijavljenim korisnicima. U izvještaj ulaze samo projekti s uključenim modulima zadataka i vremena.

## Skupno uređivanje, kopiranje i premještanje

Na popisu zadataka označite do 100 zadataka i odaberite novi status, prioritet ili oba. Sve se izmjene spremaju u jednoj transakciji uz provjeru ovlasti, dopuštenih prijelaza i revizija. Ako bilo koja izmjena ne prođe, cijela se grupa poništava.

Kopiranje s detalja zadatka traži pregled izvornog i pravo kreiranja na odredišnom projektu. Kopija prenosi naslov, opis, tip, prioritet i rok te počinje u statusu **Novo**, bez nositelja, komentara i utrošenog vremena. Za premještanje su potrebni pravo uređivanja na izvornom projektu, pravo kreiranja na odredišnom i pravo upravljanja obama projektima. Premještanje zadržava ID, status, komentare, vrijeme i povijest. Nositelj ostaje dodijeljen ako je izravni član odredišnog projekta.

## Veze zadataka

Nakon migracije `015-issue-links.sql` na detaljima zadatka unesite broj drugog zadatka i odaberite vrstu veze. Za dodavanje ili uklanjanje veze potrebna je ovlast uređivanja obaju zadataka. Korisnik vidi vezu ako može otvoriti oba zadatka. Povezanost i duplikati prikazuju se s obje strane. Podzadatak ima najviše jedan nadređeni zadatak. Hijerarhijske veze i ovisnosti ne dopuštaju cikluse. Veza **Blokira** prikazuje ovisnost, a prijelaze statusa i dalje određuje tijek rada.

## Osobni spremljeni upiti

Nakon migracije `016-saved-issue-queries.sql` primijenite filtre na popisu zadataka, unesite naziv u odjeljak **Moji spremljeni upiti** i spremite ih. Upit pamti projekt, status i tekst pretrage. Vlasnik ga može ponovno otvoriti ili ukloniti. Rezultati pri svakom otvaranju ovise o tadašnjim projektnim ovlastima.

Migracija `017-saved-issue-query-layout.sql` omogućuje spremanje odabranih stupaca, sortiranja i grupiranja uz filtre. Postojeći upiti zadržavaju zadani prikaz. Popis prikazuje ukupni broj rezultata i broj zadataka po grupi. CSV zadržava standardne stupce i primjenjuje filtre i sortiranje.

## Kalendar i Gantt

Nakon migracije `018-issue-start-dates.sql` zadatak može imati neobavezni datum početka. Na stranici **Kalendar rokova** otvorite **Ganttov prikaz** i odaberite mjesec i projekt. Gantt prikazuje zadatke koji se preklapaju s odabranim mjesecom. Ako je upisan samo jedan datum, zadatak se prikazuje kao jednodnevna oznaka. Ovisnosti su poveznice između zadataka koji su vidljivi u odabranom prikazu. Datumi se uređuju na detaljima zadatka.

## Projektna karta planova

Nakon migracije `019-project-milestones.sql` na detaljima projekta otvorite **Karta planova**. Korisnik s ovlasti upravljanja aktivnim projektom može dodavati i uređivati verzije i prekretnice, uključujući opcionalni ciljni datum i oznaku dovršenosti. Čitanje prati vidljivost projekta. Zatvoreni i arhivirani projekti zadržavaju pregled postojećih stavki.

Nakon migracije `020-issue-roadmap-items.sql` zadatak se može povezati s jednom verzijom ili prekretnicom svog projekta. Karta planova prikazuje broj zadataka po statusu i postotak zatvorenih. U izračun ulaze statusi koji su u tijeku rada označeni kao zatvoreni. Oznaka dovršenosti same verzije ili prekretnice uređuje se zasebno. Pregled napretka prati ovlast pregleda zadataka. Kopiranje unutar projekta zadržava poveznicu, a kopiranje i premještanje u drugi projekt uklanjaju poveznicu s izvornom stavkom.

## Privatne bilješke i obavijesti

Nakon migracije `022-issue-notes-notifications.sql` privatne bilješke dostupne su autoru na detaljima zadatka. Privatne bilješke izostavljene su iz povijesti, feedova i obavijesti. **Obavijesti** prikazuju do 50 novih i pročitanih aktivnosti na praćenim zadacima. Aktivnost autora vlastite radnje izostavlja se iz njegovih obavijesti. Prestanak praćenja uklanja obavijesti za zadatak. Vidljivost obavijesti ovisi o aktualnim projektnim ovlastima i postavkama modula.

## Revizijski zapis administracije

Puni administrator može otvoriti **Role i ovlasti → Revizijski zapis** i pregledati do 100 zadnjih zapisa. Migracije `023-administration-audit.sql` i `024-audit-details.sql` dodaju zapise o računima, ulogama, grupama, članstvima, organizaciji, projektima, tijeku rada i karti planova. Zapisi nastaju u istoj SQL transakciji s promjenom. Reset lozinke bilježi radnju i ciljni račun.

## Projektne novosti

Nakon migracije `025-project-news.sql` na detaljima projekta otvorite **Novosti**. Objavljivanje zahtijeva upravljanje aktivnim projektom, a komentiranje ovlast `news.comment`. Migracija je dodjeljuje postojećim standardnim ulogama Voditelj, Developer, Moderator i Član. Pregled prati vidljivost projekta. Na zatvorenim i arhiviranim projektima postojeće objave i komentari dostupni su za čitanje.

## Projektni wiki

Nakon migracije `026-project-wiki.sql` na detaljima projekta otvorite **Wiki**. Čitanje stranica i povijesti prati vidljivost projekta. Za uređivanje aktivnog projekta potrebna je ovlast `wiki.edit`, koju migracija dodjeljuje standardnim ulogama Voditelj i Developer. Svako spremanje stvara novu verziju naslova i teksta. Na stranici možete otvoriti stariju verziju ili odabrati dvije verzije za usporedbu jednu uz drugu. Wiki prikazuje običan tekst.

## Prilozi zadataka

Nakon migracije `027-issue-attachments.sql` na detaljima zadatka možete dodavati i uklanjati priloge uz ovlast uređivanja u aktivnom projektu. Veličina pojedine datoteke je od 1 bajta do 10 MiB. Preuzimanje provjerava pristup zadatku i isporučuje datoteku kao privitak. Sadržaj je u SQL bazi. Kopija zadatka ne uključuje priloge, a premještanje ih zadržava pod ovlastima odredišnog projekta.

## Dokumenti projekta

Nakon migracije `028-project-documents.sql` na detaljima projekta otvorite **Dokumenti**. Korisnici s pravom upravljanja aktivnim projektom mogu dodati dokument s naslovom, opisom i datotekom do 10 MiB ili ga ukloniti. Pregled zbirke i preuzimanje prate vidljivost projekta. Datoteke se pohranjuju u SQL bazi i preuzimaju kao privitci. Zatvoreni i arhivirani projekti zadržavaju pregled i preuzimanje dokumenata.

## Projektni forum

Nakon migracije `029-project-forums.sql` na detaljima projekta otvorite **Forum**. Pregled prati vidljivost projekta. Za otvaranje teme i objavu odgovora potrebna je ovlast `forum.post`, a za zaključavanje i otključavanje `forum.moderate`. Zaključana tema ostaje dostupna za čitanje. Na zatvorenim i arhiviranim projektima forum je dostupan za čitanje.

## Tema prikaza

Nakon migracije `030-account-display-theme.sql` na stranici **Moj profil** odaberite svijetlu temu, tamnu temu ili postavku uređaja. Odabir se sprema na korisnički račun i primjenjuje pri sljedećem zahtjevu, uključujući prijave iz drugih preglednika. Zadana je svijetla tema.

## Prilagodljiva polja zadataka

Nakon migracije `032-project-issue-fields.sql` upravitelj aktivnog projekta može u **Poljima zadataka** dodati tekstualno polje. Na detaljima zadatka korisnici s pravom uređivanja mogu spremiti tekst do 500 znakova ili isprazniti vrijednost. Čitanje prati ovlast pregleda zadatka. Pri premještanju u drugi projekt vrijednosti izvornog projekta ostaju pohranjene i ponovno se prikazuju ako se zadatak vrati. Kopiranje stvara zadatak bez prilagodljivih vrijednosti. Sučelje je dostupno na hrvatskom i engleskom, a korisnički nazivi i vrijednosti prikazuju se kako su uneseni.

Migracija `033-custom-fields.sql` dodaje tekstualna polja za profile, projekte i pojedinačne unose vremena. Puni administrator definira polja za svaku vrstu pod **Role i ovlasti → Prilagodljiva polja**. Korisničke vrijednosti dostupne su vlasniku profila i korisnicima s ovlasti upravljanja korisnicima. Projektne vrijednosti prate vidljivost projekta, a uređuje ih upravitelj aktivnog projekta. Polja unosa vremena prate ovlast pregleda evidencije vremena, a uređuje ih autor unosa dok je evidentiranje vremena omogućeno. Prazna vrijednost briše sadržaj polja uz zadržavanje revizije. Sučelje i poruke provjere dostupni su na hrvatskom i engleskom.

Nakon migracije `035-issue-field-visibility.sql` upravitelj aktivnog projekta može u **Poljima zadataka** ograničiti pojedino prilagodljivo polje samo na upravitelje ili ga vratiti na standardnu vidljivost. Upravitelj i dalje mora imati ovlast uređivanja zadatka za promjenu vrijednosti. Postojeća polja počinju sa standardnom vidljivošću. Ograničavanje čuva vrijednost, a promjene vidljivosti ulaze u revizijski zapis.

## Privatni zadaci

Migracija `036-private-issues.sql` postojećim zadacima daje standardnu vidljivost. Pri stvaranju zadatka može se označiti **Privatni zadatak**. Naknadno vidljivost mijenja autor ili upravitelj projekta s pravom uređivanja zadatka. Uz aktualno pravo pregleda projekta privatni zadatak mogu vidjeti njegov autor, nositelj i upravitelji projekta. Praćenje zadatka ne daje pristup.

Ista provjera vrijedi za popise, pretragu, CSV, kalendar, Gantt, povezane zadatke, aktivnosti, obavijesti, priloge, evidenciju vremena i sažetke karte planova. Kopiranje i premještanje zadržavaju oznaku privatnosti. U odredišnom projektu primjenjuju se njegove aktualne ovlasti. Sučelje i upute dostupni su na hrvatskom i engleskom.

## Git repozitoriji projekta

Nakon migracije `037-project-repositories.sql` otvorite **Git repozitoriji** na detaljima projekta. Upravitelj aktivnog projekta može dodati ili ukloniti imenovanu HTTPS poveznicu na repozitorij. Adresa se unosi bez vjerodajnica, parametara i fragmenta. Pregled poveznica prati vidljivost projekta, a pristup sadržaju repozitorija provjerava njegovo Git okruženje. Dodavanje i uklanjanje ulaze u revizijski zapis. Pregled commitova i razlika unutar aplikacije preostaje za SCM-01.

## Postavke e-pošte i red čekanja

Nakon migracije `039-email-notification-queue.sql` pod **Moj profil → Obavijesti e-poštom** možete spremiti adresu i omogućiti pripremu obavijesti za aktivnosti drugih korisnika na praćenim zadacima. U red ulaze samo obavijesti za zadatke dostupne primatelju u trenutku nastanka aktivnosti. U red se spremaju identifikatori, a adresa ostaje u postavkama korisnika. Promjena adrese, isključivanje obavijesti ili prestanak praćenja uklanjaju pripadajuće zapise iz reda. Migracija `040-smtp-delivery.sql` dodaje SMTP isporuku, provjeru aktualnog pristupa prije isporuke, ponovne pokušaje i podsjetnike.

`pwsh -File scripts/Test-Smtp.ps1` koristi privremenu SQL bazu i SMTP prijamnik na lokalnoj petlji. Test provjerava isporuku, hrvatski i engleski naslov, privremeni SMTP odgovor 451 i ponovni pokušaj, provjeru pristupa prije slanja te deduplikaciju podsjetnika.

## DB-02: asinkroni Oracle dohvat

`dotnet run --project scripts/oracle-relational-tests -- <lokalne-postavke.json>` provjerava relacijsku migraciju, ciljane upite, paralelne upise, ponovno vrednovanje ovlasti nakon čekanja i rollback. Pokrenuti i na odvojenoj migracijskoj testnoj shemi; test zaštite zadnjeg administratora namjerno je ograničen na tu shemu. Uz `TEAM_MANAGER_ORACLE_URL=https://localhost:7572` uključene su HTTP provjere lokaliziranih HR/EN odgovora 503 i 403. Testne postavke moraju ostati izvan Gita.

`node scripts/Test-OracleWorkspace.cjs` provjerava privatnost, naslijeđene ovlasti, istodobno uređivanje, premještanje i povezane podatke. Binarni privitak sadrži više od 256 KiB i sve vrijednosti bajta, a preuzimanje se uspoređuje bajt po bajt i nakon naknadnih izmjena. Time se provjerava i dohvat BLOB-a većeg od jednog fetch međuspremnika. `TEAM_MANAGER_QA_URL=https://localhost:7572 node scripts/Test-Browser.cjs` provjerava HR/EN tokove, tipkovnicu i axe pravila pristupačnosti. Oracle testove koji pišu u istu shemu pokretati slijedno.

Runtime Oracle pozivi koriste asinkroni početak transakcije, ExecuteReaderAsync/ReadAsync i ExecuteScalarAsync. Tipizirani getteri čitaju već dohvaćen redak; RelationalCommand prefetchira cijele LOB vrijednosti. Callbackovi koriste isti otvoreni connection/transaction i ne pokreću paralelne naredbe na toj vezi. Za provjeru SQL Server grane pokrenuti `scripts/Test-Integration.ps1` uz `Database__Provider=SqlServer`.

Lokalno provjereno 19. 11. 2026.: Release build, obje Oracle sheme (1.875 i 1.877 arhiviranih zapisa), HTTP HR/EN 503/403, Oracle preglednički tokovi s većim BLOB-om, 18 WCAG A/AA skeniranja s HR/EN prebacivanjem i tipkovnicom te SQL integracije uključujući SMTP/IMAP. Završni lokalni paket na https://localhost:7572 vraća readiness 200.
