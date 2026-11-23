# Status izdanja Team Manager

Ažurirano nakon lokalne revizije 22. 11. 2026. SQL Server, Oracle i Db2 LUW podržavaju aktivne module: račune i ovlasti, projekte i zadatke, planove, vrijeme, prilagodljiva polja, dokumente, wiki, novosti, forum, Git poveznice/povijest, ulaznu e-poštu, obavijesti, SMTP/podsjetnike, osobne API tokene, registraciju i grupe. HR/EN sučelje je zadržano. Početni legacy pregled timskih stavki u Oracle načinu otvara aktivni pregled projekata.

## REV2-08 / REV2-09 / REV2-10 — dovršeno 23. 11. 2026.

Aktivni paket: `artifacts/rev2-polish-final`; [demo](https://localhost:7610) i [postojeći Db2 pregled](https://localhost:7589). Konfiguracija i poslovni podaci očuvani su bez migracije (shema 11 / protokol 5). Svih deset nalaza druge revizije lokalno je zatvoreno; vanjske produkcijske provjere ostaju zasebne.

- **REV2-08:** povijest prikazuje čitljive promjene polja sa starim/novim vrijednostima za stvaranje i izmjenu zadatka, ispravak vremena te premještanje. Nazivi polja, da/ne, prazne vrijednosti, datumi i sati koriste HR/EN i trenutačnu kulturu. Korisnički tekst i nazivi šifrarnika ostaju izvorni. Imena nositelja/plana koriste postojeće dopuštene odabire, a imena projekata samo vidljive projekte; nedostupan povijesni ID ostaje označen bez dodatnog otkrivanja imena. Izvorni audit JSON ostaje nepromijenjen, kodiran i dostupan u zatvorenim dodatnim detaljima. Podržani su ugrađeni JSON niz prethodne snimke i izravni objekt. Semantička usporedba izbjegava lažne izmjene zbog JSON escapinga, zapisa datuma ili broja revizije; nepoznati/stari neparsabilni zapisi ostaju dostupni u izvornom obliku.
- **REV2-09:** generička stranica nedostupnosti ima HR/EN naslov i povratak na dostupne projekte. Nedostupan privatni i nepostojeći zapis daju isti glavni sadržaj, HTTP 404 i no-store, bez odražavanja URL-a, ID-a ili privatnog imena. Ponovno usmjeravanje prolazi Razor routing; Oracle/Db2 guard dopušta stranicu i nepostojeće putanje, uz očuvanu zaštitu stvarnih nepodržanih modula. Anonimni jezik dolazi iz Accept-Language; HEAD čuva 404 bez tijela. API, health i ostale HTTP metode zadržavaju vlastite odgovore.
- **REV2-10:** prazna kasnija stranica pretrage nudi prethodnu i prvu stranicu. Poveznice čuvaju pojam i modul, uključujući posebne znakove. Na prvoj praznoj stranici nema nevaljanog povratka; postojeća navigacija nepraznih grupa ostaje očuvana.

`Test-ReviewPolish.cjs` provjerava sve tri stavke na izoliranim SQL Server/Oracle/Db2 bazama: HR/EN, izvorni JSON, kodiranje HTML-a, imena nositelja/plana, datume/sate, komentare, nepromijenjeno spremanje, mobilnih 320 px i axe; identične privatne/nepostojeće 404 odgovore za projekte/zadatke/vrijeme/putanje, anonimni/HEAD/API odgovor; prethodnu/prvu stranicu prazne pretrage s posebnim znakovima. Premještanje dodatno provjerava da promatrač u odredištu ne dobiva naziv nedostupnog izvornog projekta. Skripta stvara vlastite podatke i pokreće se samo u izoliranim bazama.

Završni paket dodatno prolazi `Test-ReturnContext.cjs` i `Test-DraftProtection.cjs` u privremenim SQL bazama te `Test-OracleApi.cjs` u izoliranoj Oracle shemi (anonimni/valjani/opozvani token, 404 bez promjene REST ugovora). Nakon postavljanja prolaze `Test-ProductDemo.cjs` s 28 HR/EN axe provjera i `Test-ReviewSmoke.cjs` nad postojećim Db2 podacima. Oba korisnička URL-a imaju readiness 200. Vizualno su pregledani HR/EN povijest i mobilni 404; dokazi su lokalno u ignoriranoj mapi `artifacts/rev2-polish-evidence`. Poslovni podaci čistog demoa nisu korišteni za regresijske upise.

## REV2-05 / REV2-07 — dovršeno 23. 11. 2026.

Aktivni paket: `artifacts/rev2-navigation-final`; [demo](https://localhost:7610) i [postojeći Db2 pregled](https://localhost:7589), readiness 200. Konfiguracija i baze očuvane su bez migracije (shema 11 / protokol 5).

- **REV2-05:** povratni kontekst pretrage i izvještaja prenosi se kroz detalje, GET/POST obrasce, validaciju i spremanje, uključujući 303 nakon prijenosa dokumenta. Čuvaju se upit, modul, stranica, filtri i sidro retka izvještaja. Prihvaća se samo jedna lokalna povratna adresa do 4096 znakova; vanjske i dvosmislene vrijednosti odbacuju se. Posebni znakovi nisu dvostruko HTML kodirani. Glavna navigacija označava izvještaj za detalj vremena i kalendar za Gantt, bez označavanja navigacije stranica izvještaja.
- **REV2-07:** prikazuju se izvorni autor i broj radnika, zadatak i projekt s poveznicama prije obrasca ispravka. Ciljani autorizirani SQL upiti svih providera vraćaju podatke koji nisu vezani uz POST. Očuvani su autor, token konkurentnosti, zaključavanje i pravila uređivanja. Prikaz bez prava uređivanja ima HR/EN poruku; opoziv pristupa primjenjuje se odmah.

`Test-ReturnContext.cjs` prolazi na privremenom SQL Serveru te izoliranim Oracle/Db2 shemama, HR/EN: osam modula pretrage, posebni znakovi, druga wiki stranica, prijenos dokumenta, druga stranica izvještaja s 51 unosom, nevaljan i valjan ispravak, povezani detalji, izvorni autor, krivotvoreni metapodaci, pregled bez uređivanja, opoziv, nepodudarni zadatak/unos, nesigurne adrese, mobilnih 320 px i axe. Skripta stvara regresijske podatke i namijenjena je samo izoliranim bazama.

Ponovljeni su SQL regresija navigacije s 52 retka i potpunim CSV-om te zaštite nacrta/preuzimanja. Konačni paket prolazi `Test-DraftProtection.cjs`, `Test-ProductDemo.cjs` (28 HR/EN axe provjera i glavni demo tokovi) i `Test-ReviewSmoke.cjs` nad postojećim Db2 podacima bez promjene poslovnih zapisa. Ostaju REV2-08/09/10; vanjski produkcijski uvjeti nisu ponovno potvrđivani.

## REV2-03 / REV2-04 / REV2-06 — dovršeno 23. 11. 2026.

Aktivni lokalni paket: `artifacts/rev2-presentation-final`; [demo](https://localhost:7610) i [postojeći Db2 podaci](https://localhost:7589). Konfiguracija i baze su očuvane; nema nove migracije (shema 11 / protokol 5).

- **REV2-03:** tablica vremena ima zaseban označen okvir s tab-stopom, vidljivim fokusom i lokalnim vodoravnim pomicanjem. Fiksne čitljive širine sprječavaju svođenje stupaca na pojedinačna slova; dodatna polja unosa vremena imaju naslov stupca. Dugi neprekinuti tekstovi u tablici i povijesti prelamaju se unutar stranice. Izmjereni `document.scrollWidth` jednak je 320 odnosno 390 px u HR/EN i obje teme, uključujući napomenu od 1000 neprekinutih znakova. Zadnji stupac dohvatljiv je tipkovnicom.
- **REV2-04:** zadane Required/StringLength poruke i standardne model-binding poruke koriste zajedničke HR/EN resurse. Nazivi aktualnih polja lokaliziraju se tijekom zahtjeva, bez zaključavanja jezika u zajednički cache metapodataka. Prilagođene postojeće poruke ostaju sačuvane. Provjereni su aktivnost, napomena, naslov zadatka, naziv projekta i lozinka te nevaljan datum/broj. Sažetak greške ostaje fokusiran, `role=alert`, a nevaljano polje označeno `aria-invalid` i povezano sa sažetkom.
- **REV2-06:** Gantt opis raspona, samo početka i samo roka koristi `CurrentCulture` kao drugi izvještaji. Osnovni HR/EN formati potvrđeni su na sva tri providera, a dodatni Db2 proces s `Formatting:Culture=en-US` potvrđuje `4/5/2027` / `4/9/2027` u oba jezika. HTML datumski ulazi ostaju ISO; geometrija vremenskog prikaza nije promijenjena.

`Test-MobileLocalization.cjs` prolazi na privremenom SQL Serveru te izoliranim Oracle/Db2 shemama: HR/EN, 320/390 px, svijetla/tamna tema, tipkovničko pomicanje, zadnji stupac, axe, serverski Required/StringLength, lokalizirani nazivi te datum/broj pretvorbe. Negativni upisi ne mijenjaju postojeći unos vremena. Poseban en-US proces prošao je isti test uz izričitu provjeru očekivanih datuma. Skripta stvara svoje regresijske projekte/zadatke i treba je pokretati samo na izoliranim lokalnim bazama.

`Test-DraftProtection.cjs` ponovljen je na konačnom SQL Server paketu i potvrđuje prethodne P1 zaštite nacrta, wiki konflikata i preuzimanja. Nakon postavljanja ponovljeni su `Test-ProductDemo.cjs` (28 HR/EN axe provjera, glavni tokovi i granice privatnosti) i `Test-ReviewSmoke.cjs` nad postojećim Db2 podacima. Dokazi mobilnog prikaza su u ignoriranoj mapi `artifacts/rev2-mobile-evidence` (snimke i measurements.json). Otvorene ostaju REV2-05/07/08/09/10; vanjski produkcijski uvjeti nisu ponovno potvrđivani.

## REV2-01 / REV2-02 — dovršeno 23. 11. 2026.

Paket `artifacts/rev2-drafts-final` postavljen je na demo [https://localhost:7610](https://localhost:7610) i postojeći Db2 pregled [https://localhost:7589](https://localhost:7589). Baza, ovlasti i konfiguracija su sačuvane; nema nove migracije (relacijska verzija ostaje 11, protokol 5).

Wiki vraća izvorni naslov/sadržaj nacrta i očekivanu reviziju umjesto zamjene nacrta novom verzijom. Aktualna spremljena verzija ostaje odvojeno prikazana. Spremanje preko nje traži izričitu potvrdu pregleda; nova konkurentna izmjena ponovo odbija spremanje i čuva nacrt. Prazni unosi i tekst s početnim/završnim razmacima ostaju u obrascu pri grešci. Odbijeni unos vremena zadržava datum, sirove sate (uključujući nevaljan tekst), aktivnost i napomenu. Obrazac sati prima decimalnu točku ili zarez uz postojeću serversku provjeru raspona i preciznosti. Vraćeni nacrti odmah se smatraju nespremljenima, i prije nove tipkovničke izmjene.

Dokumenti i privitci imaju izričite download poveznice. CSV obrazac cilja skriveni okvir kako pokušaj navigacije glavnog dokumenta ne bi aktivirao ili suspendirao njegovu zaštitu prije primitka attachment odgovora. Input/change i pageshow ponovno aktiviraju zaštitu nakon novih izmjena i vraćanja stranice. Očuvana je pojedinačna lokalizirana potvrda stvarnog odlaska, a valjan submit vlastitog obrasca prolazi bez dodatne potvrde.

`Test-DraftProtection.cjs` prošao je na privremenom SQL Serveru te izoliranim Oracle i Db2 shemama: oba jezika, dva uzastopna wiki konflikta, zabrana tihog prepisivanja, izričito pregledano spremanje, prazni/nevaljani nacrti, zadržavanje i ispravan unos vremena, stvarna preuzimanja dokumenata/privitaka/CSV-a, stvarni beforeunload pri odustanku od reloada i odsutnost dvostrukih potvrda. HR/EN konfliktne stranice prošle su axe provjeru. Skripta stvara regresijske podatke i treba je pokretati samo na izoliranim lokalnim bazama, ne na čistom demou ili radnim podacima.

Nakon postavljanja, `Test-ProductDemo.cjs` ponovno je prošao 28 HR/EN axe provjera, glavne tokove i granice projektne privatnosti, a `Test-ReviewSmoke.cjs` provjerio je postojeći radni Db2 pregled bez promjene poslovnih zapisa. Oba URL-a imaju readiness 200. Preostale stavke REV2-03–REV2-10 ostaju otvorene; vanjski produkcijski uvjeti nisu promijenjeni.

## Druga revizija — 23. 11. 2026.

Pregledan je paket `artifacts/review-release` (commit `1f174a0`) na [https://localhost:7610](https://localhost:7610), Db2 LUW. Pokrenuti su postojeći VM, Db2 kontejner i demo nakon restarta računala; postojeća baza nije ponovno stvarana. Pregledano je 44 HR/EN prikaza, projektne granice triju demo uloga, običan odustanak od izmjena, odbijeni upisi, konflikt wiki revizije, preuzimanje datoteke, mobilni prikaz i 24 automatizirane provjere pristupačnosti u svijetloj/tamnoj temi. Axe nije prijavio povrede u tim provjerama, ali zasebno je potvrđen problem reflowa na zadatku.

**Deset nalaza prvotnog pregleda (P1 naknadno zatvoreni iznad):** dva P1 (gubitak nacrta kod konflikta/greške i neaktivna zaštita novih izmjena nakon preuzimanja), pet P2 te tri P3. Detalji: [PRODUCT-REVIEW-2026-11-23.md](PRODUCT-REVIEW-2026-11-23.md); redoslijed: [BACKLOG.md](BACKLOG.md). U provjerenim scenarijima nema novog otkrivanja privatnog projektnog članstva. Ranije zatvaranje REV-01–REV-14 ne pokriva dodatne grane pronađene ovom revizijom. U samom izvornom pregledu izrađeni su izvještaj i prioriteti; naknadna P1 implementacija i provjere opisane su iznad. SQL Server, Oracle, IIS/OpenShift, NVDA i produkcijski kapacitet nisu ponovno provjeravani.

## Završetak prve revizije 22. 11. 2026.

Svih 14 stavki REV-01–REV-14 je lokalno implementirano i provjereno. Novi demo radi na [https://localhost:7610/Projects](https://localhost:7610/Projects), postojeći Db2 podaci na [https://localhost:7589/Projects](https://localhost:7589/Projects). Paket: artifacts/review-release; Oracle/Db2 relacijska verzija 11, protokol zaključavanja 5. Postojeći Db2 pregled je nadograđen uz očuvanje konfiguracije i baze. Detalji ponovljivog demo postupka: [PRODUCT-DEMO.md](PRODUCT-DEMO.md).

Dvije neovisne demo baze/sheme prošle su usporedbu po 233 zapisa. Test-ProductDemo.cjs prošao je 28 HR/EN axe provjera, glavne tokove, privatni projekt/profil, detalje vremena i obavijesti; Test-ReviewSmoke.cjs provjerio je postojeće Db2 podatke bez izmjene poslovnih zapisa. Test-TimeCorrection.cjs ponovljen je i na konačnom SQL Server paketu. Eksplicitni DECIMAL SQL zbroj uklanja grešku inicijalizacije Db2 decimalnog upravljačkog programa pri hladnom pokretanju na hr-HR. Statički JS koristi webroot objavljenog paketa i kada se konfiguracija nalazi u drugoj mapi. Preostali vidljivi datumi plana, tokena, IMAP nadzora i commitova koriste regionalni format (offset commita ostaje vidljiv).

IIS konfiguracija u ovoj sesiji nije promijenjena: Windows administratorski token nije dostupan. Ovi završni URL-ovi koriste lokalni dotnet HTTPS proces. Automatizirana pristupačnost nije zamjena za novu ručnu potvrdu čitačem zaslona. Bankarski pristupi, ciljni IIS/OpenShift i Db2 z/OS ostaju vanjski uvjeti prihvata.

## Ranije lokalne provjere (povijesni paketi i URL-ovi)

**REV-10, 22. 11. 2026.:** proširena pretraga radi u HR/EN na sva tri providera. `Test-ContentSearch.cjs` provjerava sadržaj wikija, metapodatke datoteka, tijelo novosti i foruma, nastavak nakon 20 rezultata te trenutno uklanjanje rezultata nakon opoziva članstva. Pretraga datoteka obuhvaća naziv/opis/ime datoteke, ne tekst binarnog priloga. Povijest wikija pretražuje se kroz trenutačnu verziju.

**REV-03/06/07/11, 22. 11. 2026.:** test s 52 zadatka potvrđuje 50+2 rezultata, izvoz svih 52 u CSV, povratak sa zadatka na iste filtre/stranicu i odbijanje vanjskog povratnog URL-a. Projekti imaju filtar, pregled i odvojene postavke; kartice imaju lokalizirane naslove i navigacija označava trenutačni odjeljak. SQL Server i Db2 provjere prošle su na paketu `rev-navigation-final`, Oracle i na završnom paketu s ciljanim dohvatom odredišta prijenosa (`rev-content-final`). Svaka izmjena zadatka i dalje ponovno provjerava prava unutar transakcije.

**REV-02/04/05/08, 22. 11. 2026.:** uvedena je zaštita nespremljenih POST obrazaca, fokus i uloga upozorenja na serverskim greškama, dopunjeni pronađeni EN prijevodi i usklađena ograničenja šifrarnika. `Test-FormRevision.cjs` prolazi na SQL Serveru, Oracleu i Db2, uključujući HR/EN i axe provjeru stanja greške. To nije nova ručna NVDA potvrda. Regionalni format može se zadati neovisno od jezika kroz `Formatting:Culture` (zadano hr-HR/en-GB).

**REV-01 dovršen 22. 11. 2026.:** dohvat projektnih članstava sada uključuje promatrača i primjenjuje serverske ovlasti. `Test-ProfilePrivacy.cjs` prošao je na privremenom SQL Serveru te izoliranim Oracle i Db2 shemama u HR/EN, uključujući javni, vlastiti, administratorski, naslijeđeni i opozvani pristup. Naknadne dorade zatvorene su u završetku revizije iznad.

**Revizija proizvoda 20. 11. 2026.:** pronađeno je 14 otvorenih dorada, uključujući P1 otkrivanje naziva privatnih projekata i članskih uloga kroz tuđi profil. Na Db2-u je potvrđeno da ograničeni korisnik te metapodatke vidi kroz profil, dok izravan pristup provjerenom privatnom zadatku ostaje HTTP 404. Prethodni prolaz regresija ne pokriva taj nalaz. Prije korisničkog pilota zatvoriti P1 stavke iz [revizije proizvoda](PRODUCT-REVIEW-2026-11-20.md).

- Raniji paket: `artifacts/oracle-complete-final`, Oracle shema 10 / protokol zaključavanja 5.
- URL: https://localhost:7572/Projects; readiness 200.
- Oracle integracije potvrđuju migracijski integritet 51 tablice, očuvanje podataka/povijesti, ovlasti, zaključavanja, paralelne upise, rollback, opozive i zaštitu zadnjeg administratora.
- Stvarni loopback TLS IMAP i SMTP testovi potvrđuju stvaranje privatnih zadataka, deduplikaciju, commit/Seen, HR/EN poruke, ponovne pokušaje, lease zaštitu i podsjetnike.
- Cjeloviti Oracle browser tokovi i 52 automatizirana WCAG A/AA skeniranja su prošli; odvojeni API, registracijski, grupni i e-mail testovi provjeravaju dodatne autentikacijske i UI tokove.
- SQL integracijske regresije i zasebni privremeni SQL testovi registracije/grupa su prošli.
- Raniji Db2 Windows paket: `artifacts/db2-complete-final`, [pregled](https://localhost:7589/Projects), readiness 200. Linux slika `team-manager:db2-qa` radi kao UID 1654 iza [HTTPS proxyja](https://localhost:7591/Projects); sesija je potvrđena nakon restarta. Upute: [DB2-LOCAL.md](DB2-LOCAL.md).
- Nova Db2 testna shema `TMR_QA_CE9D1C4885C8` prošla je migraciju, integritet i runtime/SMTP testove; testne snimke i lozinke nisu u Gitu.
- Zaštitna lista Oracle ruta ostaje uključena. Svi aktivni moduli omogućeni su pojedinačno; neprovjereni/razvojni endpointi nisu globalno otvoreni.

## Zatvaranje lokalnog backloga i preostale ciljne provjere

| Stavka | Stvarni status |
|---|---|
| DB-02 / Db2 LUW | Lokalno dovršeno: službeni .NET 10 driver, 51 tipizirana tablica, svježa migracija i neovisna usporedba 10.465 zapisa, zamrznuta arhiva, ciljani SQL, sekvence i aplikacijski lockovi. Provjereni glavni moduli, ovlasti, paralelni upisi/opozivi, SMTP/podsjetnici, 52 HR/EN WCAG skeniranja i Linux runtime iza HTTPS proxyja sa sesijom nakon restarta. Ciljna produkcijska provjera ostaje otvorena. |
| DB-02 / Db2 z/OS | Nije implementirano/provjereno na ciljnoj bazi; potreban je ciljni pristup i odgovarajuća Db2 Connect licenca. |
| AUTH-06 | Lokalni Keycloak/OpenLDAP scenariji postoje; bankarski SSO/AD ostaje za ciljno okruženje. |
| MAIL-01 | Lokalni gateway i TLS IMAP su provjereni; namjenski stvarni sandučić nije povezan/provjeren. |
| OPS-02 / OPS-03 | Lokalni IIS/HTTPS, backup/restore i rollback ranije provjereni; ciljni IIS i operativni nadzor nisu potvrđeni. |
| OPS-04 | Lokalna slika/proxy ranije provjereni; ciljni OpenShift nije dostupan u ovom razgovoru. |
| QA-02 | Automatizirane lokalne provjere prošle; kapacitet i završna prihvatna provjera na ciljnom produkcijskom okruženju ostaju otvoreni. |

Ovaj status ne predstavlja potvrdu spremnosti za bankarsku produkciju. DB-02 i vanjske provjere nisu zatvoreni samim pushom na GitHub. Potvrda registracije e-poštom ostaje izvan dogovorenog opsega AUTH-05.

### Revizija: vrijeme i stariji zapisi — 22. 11. 2026.
REV-13/REV-14: kontrolirani ispravci vremena s izvornim autorom i poviješću vrijednosti, zaštitom od zastarjelog spremanja i ovlastima; detalji izvještaja i straničenje/pretraga obavijesti i revizije. Test-ReportPages.cjs i Test-TimeCorrection.cjs prolaze na SQL Serveru, Oracleu i Db2, HR/EN. Nova ruta /TimeEntry uključena je u postojeći popis podržanih Oracle/Db2 ruta; zaštita ostaje aktivna.

### Šifrarnici — 22. 11. 2026.
REV-09: administrativni poredak i aktivnost vrijednosti, očuvanje povijesnih zadataka, transakcijska provjera dodjele i revizijski zapis. Test-CatalogRevision.cjs prolazi HR/EN na SQL Serveru, Oracleu i Db2. Prije nove aplikacije primijeniti --apply-schema za SQL Server odnosno --migrate-oracle-workspace za postojeće Oracle/Db2 sheme (verzija 11, protokol zaključavanja ostaje 5). Migracija je aditivna i ponovljiva; stare aplikacije trebaju se zaustaviti pri produkcijskoj zamjeni.
