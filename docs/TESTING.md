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
