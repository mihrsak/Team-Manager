# Backlog Team Manager

## Osma revizija — 26. 11. 2026.

REV8-01/02/03 implementirani i provjereni na izoliranim SQL Server, Oracle i Db2 bazama uz HR/EN: oporavak nacrta zadatka nakon ponovljenog sukoba, očuvanje odbijenih komentara i odvojene privatne bilješke. IIS objava i završni prihvat ostaju otvoreni jer Windows administratorsko pokretanje nije potvrđeno; port 7443 još koristi prethodni paket. Detalji: [PRODUCT-REVIEW-2026-11-26-R8.md](PRODUCT-REVIEW-2026-11-26-R8.md).


## Sedma revizija — dovršeno 25. 11. 2026.

Nova revizija stvarnog [IIS pregleda](https://localhost:7443), početno 0f1cef4, donijela je tri nalaza. Sva tri popravljena su uz HR/EN i provjere na IIS-u te izoliranim SQL Server/Oracle/Db2 bazama. Paket: `artifacts/review7-final`. Reprodukcije i dokazi: [PRODUCT-REVIEW-2026-11-25-R7.md](PRODUCT-REVIEW-2026-11-25-R7.md).

| Stavka | Prioritet | Status |
|---|---|---|
| REV7-01 | P1 | Zatvoreno: zaštićeni vraćeni nacrti projekta, sirovi tekst i izvorne revizije, aktualni kontekst/hijerarhija i izričit pregled prije ponovnog upisa; drugi sukob, odlazak/reload, kodiranje i opoziv prava. |
| REV7-02 | P2 | Zatvoreno: odabiri modula i izvorna revizija ostaju nakon sukoba, aktualni odabiri prikazani su odvojeno; izričit pregled, ponovljeni sukob i 400 bez upisa za nevaljano vezanu reviziju. |
| REV7-03 | P2 | Zatvoreno: puna administracija provjerava se u bazi bez ponavljanja istog upita za svaki roditeljski projekt. Pojedinačna prava drugih korisnika i ponovna autorizacija upisa ostaju očuvani. |

Ponovljeni pregled 82 HR/EN prikaza nema novih axe prijava, prelijevanja cijelog dokumenta ni JS grešaka. Prolaze glavni IIS tokovi i prethodne regresije polja/skupnih radnji. Novi lokalni uzorak opterećenja: 1606 odgovora bez HTTP grešaka, p95 448 ms pri 25 paralelnih zahtjeva; nije produkcijski kapacitet niti usporedba s ranijim mjerenjem. Ciljni uvjeti ostaju zasebni.

## Lokalni IIS i NVDA — dovršeno 25. 11. 2026.

Aktualni paket radi na stvarnom [lokalnom IIS-u](https://localhost:7443), uz očuvanu testnu bazu i konfiguraciju. Prolaze glavni HR/EN tokovi, nacrti/sukobi/opoziv prava, no-JS prijenos i 413, ručni NVDA pregled te 24 dodatne stvarne NVDA govorne provjere. Kratko opterećenje: 2710 odgovora bez HTTP grešaka, p95 301 ms pri 25 paralelnih zahtjeva. Dokazi, opseg i ograničenja: [LOCAL-IIS-NVDA-2026-11-25.md](LOCAL-IIS-NVDA-2026-11-25.md).

| Stavka | Status |
|---|---|
| QA-LOCAL-01 | Zatvoreno: ponovno izvođenje SQL instalacije nakon proširenja kataloga; eksplicitni stupci u migraciji i paketna regresija bez duplikata. |
| QA-LOCAL-02 | Zatvoreno: prazan neobvezni opis projekta sada prolazi stvarnu HR/EN pregledničku predaju. |
| QA-LOCAL-03 | Zatvoreno: IIS čuva lokalizirane aplikacijske greške; 32 MiB zahtjev bez JS-a vraća HR/EN 413 i povratak na tekst. Datoteke ostaju ograničene na 10 MiB, upravljano tijelo na 30.000.000 B; vanjski IIS limit je 64 MiB. |

Lokalni IIS/NVDA dio navedenih tokova zatvoren je; ciljni bankarski IIS/OpenShift i produkcijski kapacitet ostaju zasebni uvjeti.

## Šesta revizija — bez novih lokalnih nalaza 25. 11. 2026.

Nakon zatvaranja REV5-01–05 ponovljen je pregled paketa `artifacts/rev5-complete` (kod e783051): 82 HR/EN prikaza i dodatnih 28 demo axe provjera, mobilnih 320 px, tri demo uloge i izolirane regresije. U provjerenom lokalnom opsegu nema novih potvrđenih nedostataka ni novih REV6 stavki. Detaljan opseg i ograničenja: [PRODUCT-REVIEW-2026-11-25-R6.md](PRODUCT-REVIEW-2026-11-25-R6.md). Ciljne produkcijske provjere ostaju zasebne.

## Peta revizija — dorade dovršene 25. 11. 2026.

Pregled commita ed45f22 pronašao je pet nalaza. Svi su sada lokalno zatvoreni u paketu artifacts/rev5-complete, HR/EN. Nacrti i skupne radnje prolaze SQL Server/Oracle/Db2 regresiju, uključujući ponovljene sukobe i opoziv prava. Reprodukcije i kriteriji: [PRODUCT-REVIEW-2026-11-25-R5.md](PRODUCT-REVIEW-2026-11-25-R5.md).

| Stavka | Prioritet | Status |
|---|---|---|
| REV5-01 | P1 | Dovršeno: sačuvani sirovi nacrti polja, izvorne revizije, aktualna vrijednost i zaštita odlaska. Izričit pregled prije ponovne predaje; novi sukob ponovno odbija upis. HR/EN, sva tri providera. |
| REV5-02 | P2 | Dovršeno: vraćeni odabir, status/prioritet i kontekst popisa, uključujući zadatak koji više nije u filtru. Aktualne revizije tek nakon potvrde pregleda; ponovljeni sukob i opoziv prava na sva tri providera. |
| REV5-03 | P2 | Dovršeno: JS provjera veličine zadržava tekst prije slanja; iznad HTTP limita HR/EN 413 s uputom za povratak, bez podizanja limita. Bez JS-a Edge povratkom vraća tekst; proxy koji odbija prije aplikacije ostaje ciljna provjera. |
| REV5-04 | P3 | Dovršeno: registracijska greška vraća sigurno kodirano ime/broj radnika, prazne lozinke i fokus na prevedenoj grešci. |
| REV5-05 | P3 | Dovršeno: opisni HR/EN naslovi registracije i početne promjene lozinke. |

Svih pet nalaza lokalno je zatvoreno. Prethodni REV4 kriteriji ostaju potvrđeni; novi opseg uključuje druge obrasce i HTTP limit prije obrade dokumenta. Ciljni IIS/OpenShift, NVDA i bankarske integracije ostaju zasebne provjere.

## Četvrta revizija — status dorada 25. 11. 2026.

Pregled aktualnog paketa rev3-navigation-complete (aed6e3d / kod 10c8943) na Db2 demou: 80 HR/EN prikaza i axe provjera, tri uloge, mobilnih 320 px, negativni unosi dokumenata, filtri i CSV. Pet nalaza: jedan P1, jedan P2 i tri P3. REV4-01–REV4-05 sada su dovršeni i provjereni na SQL Serveru, Oracleu i Db2, HR/EN. Svih pet nalaza četvrte revizije lokalno je zatvoreno. Dokazi i kriteriji: [PRODUCT-REVIEW-2026-11-24-R4.md](PRODUCT-REVIEW-2026-11-24-R4.md).

| Stavka | Prioritet | Status / predložena dorada |
|---|---|---|
| REV4-01 | P1 | Dovršeno: sirovi naslov/opis i početni prazni retci ostaju nakon greške naslova, veličine ili nedostajuće datoteke; zaštita odlaska/reloada i HR/EN uputa za ponovni odabir. Ispravljena predaja stvara jedan dokument. Sva tri providera. |
| REV4-02 | P2 | Dovršeno: nevaljani/ponovljeni filtri daju lokaliziranu grešku bez rezultata i skupnih radnji; eksplicitni nevaljan odabir, generički 404 za nedostupan projekt i 400 za nevaljan CSV. Provjera prije spremanja upita/izmjene; ponovno provjeren spremljeni upit nakon opoziva. HR/EN, sva tri providera. |
| REV4-03 | P3 | Dovršeno: HR zadržava postojeća zaglavlja/zadaci.csv, EN koristi prevedena zaglavlja/issues.csv; očuvani stupci, izvorni podaci, UTF-8 BOM, ISO datumi, escaping i zaštita formula. Sva tri providera; SQL regresija 52 retka. |
| REV4-04 | P3 | Dovršeno: opisni HR/EN naslovi kataloga i vrijednosti polja, registracijskih zahtjeva i grešaka; vlastiti/tuđi profil s autoriziranim imenom. Sigurno HTML kodiranje i generički privatni 404, sa i bez JS-a. |
| REV4-05 | P3 | Dovršeno: katalog definicija ima zasebnu administratorsku poveznicu i aktivnu sekundarnu oznaku; pojedinačne vrijednosti čuvaju projektni/vremenski/osobni kontekst. Ovlasti i zabrana pristupa bez pune administracije ostaju očuvane. |

Četvrta revizija lokalno je dovršena. Regresija izvoza/naslova/kataloga: scripts/Test-ExportTitlesCatalog.cjs na izoliranim bazama. Ciljne integracijske i produkcijske provjere ostaju zasebne. Regresija: scripts/Test-DocumentDraftsFilters.cjs na izoliranim bazama. Prethodni REV3 popravci prolaze provjerene regresije; novi nalazi obuhvaćaju dodatne scenarije izvan prethodnih kriterija. Svježe pozitivne provjere upisa/povratka provedene su u privremenim SQL bazama, dok je demo sačuvan. Naknadne Oracle/Db2/SQL provjere obuhvaćaju ove popravke; vanjski produkcijski uvjeti nisu ponovno potvrđivani.

## Treća revizija — status dorada 24. 11. 2026.

Pregled commita c68fa47 na lokalnom Db2 demou: 60 HR/EN prikaza, 320 px i desktop, tri demo uloge, odbijeni unosi bez poslovnih upisa. Dodatno prolaze prethodni tokovi upisa/nacrta na izoliranoj Db2 shemi i 28 završnih HR/EN demo axe provjera. Pronađeno je šest nalaza: jedan P1, tri P2 i dva P3. Izvještaj, reprodukcije i kriteriji prihvata: [PRODUCT-REVIEW-2026-11-24.md](PRODUCT-REVIEW-2026-11-24.md). REV3-01–REV3-06 sada su dovršeni i provjereni na SQL Serveru, Oracleu i Db2, HR/EN. Svih šest nalaza treće revizije lokalno je zatvoreno; prethodna zatvaranja REV2 odnose se na ranije pokriveni opseg.

| Stavka | Prioritet | Status / predložena dorada |
|---|---|---|
| REV3-01 | P1 | Dovršeno: sirovi nacrti teme/odgovora, novosti/komentara i plana; zaštita odlaska i reloada; izvorna revizija, prikaz aktualne stavke i izričit pregled prije zamjene, uključujući drugi sukob. HR/EN, SQL Server/Oracle/Db2. |
| REV3-02 | P2 | Dovršeno: obične tablice imaju čitljive širine, označeno lokalno pomicanje i fokus tipkovnice; zadaci, vrijeme, projekti, korisnici, audit i aktivnosti. HR/EN, 320/390 px, obje teme, SQL Server/Oracle/Db2. |
| REV3-03 | P2 | Dovršeno: samo prazan 404 pokreće generičku stranicu; sačuvani 400/401/403/405/409/429/500/503, postojeće tijelo i zaglavlja. Middleware test i stvarni HR/EN GET/HEAD 400/404 na sve tri baze, API/health bez promjene. |
| REV3-04 | P2 | Dovršeno: mjesec, projekt i sidro izvornog retka ostaju nakon validacije i spremanja; vrijeme vraća na zadatak s njegovim prethodnim kontekstom. Izvještaj otvoren izravno ostaje vlastiti izvor; lokalne adrese i ovlasti provjerene. HR/EN, sva tri providera. |
| REV3-05 | P3 | Dovršeno: sistemska nedodijeljena grupa koristi HR/EN resurs trenutačnog zahtjeva; stvarna prikazna imena i brojevi radnika ostaju izvorni. SQL Server/Oracle/Db2. |
| REV3-06 | P3 | Dovršeno: poslužiteljsko označavanje glavne i administrativne navigacije; projektna/vremenska/osobna i tuđa korisnička polja, vlastiti/tuđi profil, tokeni i e-pošta. Isti HR/EN rezultat bez JavaScripta; 404/odbijen pristup bez lažne oznake. |

Završni lokalni prihvat nakon treće revizije je dovršen: [LOCAL-ACCEPTANCE-2026-11-24.md](LOCAL-ACCEPTANCE-2026-11-24.md). Sljedeće su ciljne integracijske i produkcijske provjere iz osnovnog backloga. Regresija lokalizacije/navigacije: scripts/Test-LocalizedNavigation.cjs na izoliranim bazama. Mobilna/navigacijska regresija: scripts/Test-MobileReturnContext.cjs na izoliranim bazama. Regresije: scripts/Test-CollaborationDrafts.cjs na izoliranim bazama te scripts/Test-BrowserStatusPages.ps1. Početni pregled proveden je na Db2-u; naknadne SQL Server/Oracle provjere obuhvaćaju ove popravke, a ne cijelo produkcijsko okruženje. Ciljni IIS/OpenShift, NVDA, kapacitet i bankarske integracije nisu ponovno potvrđivani. Postojeća demo konfiguracija i poslovni podaci ostaju očuvani.

## Druga revizija — status dorada 23. 11. 2026.

Pregled demo okruženja na Db2-u, 44 HR/EN prikaza, tri ograničene uloge i 24 axe provjere. Pronađeno je deset nalaza; REV2-01–REV2-10 sada su dovršeni i provjereni na SQL Serveru, Oracleu i Db2, HR/EN. Svih deset nalaza druge revizije lokalno je zatvoreno. Dokazi, reprodukcije, vrste nalaza i kriteriji prihvata: [PRODUCT-REVIEW-2026-11-23.md](PRODUCT-REVIEW-2026-11-23.md). Početni pregled bio je na Db2-u; naknadne SQL Server/Oracle provjere obuhvaćaju navedene popravke, a ne cijelo produkcijsko okruženje.

| Stavka | Prioritet | Status / predložena dorada |
|---|---|---|
| REV2-01 | P1 | Dovršeno: sačuvani sirovi nacrt i izvorna wiki revizija, izričita potvrda nakon pregleda aktualne verzije, zaštita ponovnog konflikta; vraćena polja nevaljanog vremena i upozorenje pri odlasku s vraćenog nacrta. HR/EN, sva tri providera. |
| REV2-02 | P1 | Dovršeno: dokumenti/privitci koriste download poveznice; CSV se preuzima u odvojenom skrivenom okviru bez navigacije uređivane stranice; nove izmjene i povratak iz povijesti ponovno aktiviraju zaštitu. HR/EN stvarna preuzimanja, odustanak od reloada i pojedinačna potvrda odlaska, sva tri providera. |
| REV2-03 | P2 | Dovršeno: tipkovnicom dostupan zasebni okvir tablice vremena, čitljive širine i označeni stupci; prelamanje dugog korisničkog sadržaja i povijesti. HR/EN, 320/390 px, svijetla/tamna tema, SQL Server/Oracle/Db2. |
| REV2-04 | P2 | Dovršeno: zajedničke HR/EN Required/StringLength i model-binding poruke, lokalizirani nazivi polja uz jezik trenutačnog zahtjeva. Provjereni obavezna aktivnost/lozinka/naziv, predugi naslov/aktivnost/napomena, nevaljan datum/broj te fokus i ARIA greške, sva tri providera. |
| REV2-05 | P2 | Dovršeno: siguran lokalni povratak kroz osam modula pretrage, obrasce, validaciju i spremanje; očuvani filtri, stranica i redak izvještaja. Aktivna glavna navigacija. HR/EN, SQL Server/Oracle/Db2. |
| REV2-06 | P2 | Dovršeno: raspon, samo početak i samo rok koriste CurrentCulture kao izvještaji; HR/EN i posebna en-US Formatting:Culture provjereni neovisno od jezika. ISO ulazni datumi ostaju nepromijenjeni. |
| REV2-07 | P2 | Dovršeno: autor s brojem radnika, povezani zadatak i projekt iz autoriziranog upita; prikaz bez prava uređivanja. Očuvani autor, token i ovlasti. HR/EN, sva tri providera. |
| REV2-08 | P3 | Dovršeno: lokalizirani stari/novi iznosi i vrijednosti, dostupna imena nositelja, plana i projekata; promjene zadatka, ispravka vremena i premještanja. Izvorni JSON je sačuvan u dodatnim detaljima; nema lažnih promjena zbog escapinga, datuma ili revizije. |
| REV2-09 | P3 | Dovršeno: generički HR/EN 404 s povratkom na projekte; isti prikaz za nedostupni i nepostojeći zapis, bez imena/ID-a zapisa. Očuvani status, no-store, anonimni i HEAD odgovor te API ugovor. SQL Server/Oracle/Db2. |
| REV2-10 | P3 | Dovršeno: prazna kasnija pretraga ima prethodnu i prvu stranicu uz očuvanje pojma i modula, uključujući posebne znakove i sve module. HR/EN, sva tri providera. |

Druga revizija lokalno je dovršena. Regresija završnih stavki: scripts/Test-ReviewPolish.cjs na izoliranim SQL Server/Oracle/Db2 bazama, HR/EN, mobilni prikaz i axe. Vanjske ciljne provjere iz osnovnog backloga ostaju zasebne. Regresija povratnog konteksta i detalja vremena: scripts/Test-ReturnContext.cjs, isključivo na izoliranim bazama. Nove regresije: scripts/Test-MobileLocalization.cjs; pokretati na izoliranim lokalnim bazama. Regresija P1 popravaka: scripts/Test-DraftProtection.cjs na izoliranim testnim bazama; glavni demo i postojeći radni Db2 pregled nadograđeni su bez promjene konfiguracije ili migracije baze. Poslovni podaci demoa nisu mijenjani radi stvaranja regresijskih primjera; jezik i tema administratora vraćeni su na početne postavke.

## Prva revizija proizvoda — lokalno dovršene dorade 22. 11. 2026.

### Napredak implementacije

| Stavka | Status | Provjera |
|---|---|---|
| REV-01 | Dovršeno 22. 11. 2026. | SQL Server, Oracle i Db2: HR/EN HTML ne sadrži privatno projektno članstvo neovlaštenog promatrača; očuvani javni, vlastiti, administratorski i naslijeđeni pristup; opoziv se primjenjuje odmah. |
| REV-02 / REV-04 | Dovršeno 22. 11. 2026. | HR/EN ostanak/odustajanje, očuvanje izvornog zadatka, fokus na upozoravajući sažetak pogreške i automatizirana pristupačnost stanja greške; SQL Server, Oracle i Db2. |
| REV-05 / REV-08 | Dovršeno 22. 11. 2026. | Prevedeni pronađeni sistemski tekstovi profila, povijesti, Git/LDAP oznaka; ograničenja 50/30/20 u obrascu i na poslužitelju. |
| REV-03 / REV-06 / REV-07 / REV-11 | Dovršeno 22. 11. 2026. | Rezultati prije sekundarnih radnji, 50 redaka po stranici, pretraga projekata, potpuni CSV, validirani lokalni povratni URL-ovi, regionalni datumi, aktivna navigacija, naslovi kartica i odvojeni pregled/postavke projekta. SQL Server, Oracle i Db2; HR/EN. |
| REV-10 | Dovršeno 22. 11. 2026. | Pretraga wiki sadržaja, metapodataka dokumenata, novosti te tema/odgovora foruma; nastavak rezultata i SQL provjera projektnih prava. HR/EN, Unicode i doslovni `%_`, SQL Server/Oracle/Db2 te opoziv članstva. Binarni sadržaj datoteka nije indeksiran. |
| REV-13 / REV-14 | Dovršeno 22. 11. 2026. | HR/EN: ispravak vlastitog unosa ili unos kojim upravlja voditelj, provjera ovlasti pri spremanju, zaštita od zastarjelog spremanja, prethodne/nove vrijednosti u povijesti, očuvani autor i ID/povezana polja. Detalji izvještaja vremena; filtrirane starije stranice obavijesti (50) i administrativne revizije (100). SQL Server, Oracle i Db2. |
| REV-09 | Dovršeno 22. 11. 2026. | Poredak i deaktivacija šifrarnika u HR/EN administraciji; audit prethodnih/novih vrijednosti; povijesne vrijednosti ostaju, nove dodjele se odbijaju i pri ručnom POST-u. Novo ostaje aktivan. SQL Server migracija 43, Oracle/Db2 relacijska verzija 11; provjereno na sve tri baze. |
| REV-12 | Dovršeno 22. 11. 2026. | Dvaput stvorena zasebna demo SQL baza i Db2 shema bez prepisivanja izvora; svaki uvoz neovisno usporedio 233 zapisa. Tri projekta, osam zadataka, uloge, plan, wiki/povijest, dokument, novost, forum i vrijeme. Konačni demo: 28 HR/EN axe provjera, ovlasti i obavijesti. |

REV-01 filtrira projektna članstva na poslužitelju prema trenutačnom promatraču; odabir projekata za upravljanje članstvima zasebno vraća samo upravljive projekte. Svih 14 stavki REV-01–REV-14 lokalno je dovršeno i provjereno.

Revizija od 20. 11. 2026. zabilježila je 14 stavki; njihovo zatvaranje evidentirano je iznad, odvojeno od ciljnih provjera u tablici ispod. Dokazi, prioriteti i kriteriji prihvata: [PRODUCT-REVIEW-2026-11-20.md](PRODUCT-REVIEW-2026-11-20.md).

Izvorni prijedlog prioriteta (20. 11. 2026.): prije korisničkog pilota predloženi su **REV-01** (privatni projektni metapodaci kroz profile), **REV-02** (nespremljene izmjene), **REV-03** (pristup rezultatima zadataka), **REV-04** (povratna informacija nakon greške) i **REV-12** (zasebni demo podaci). Ostale stavke REV-05–REV-11 i REV-13–REV-14 predložene su za sljedeću iteraciju. Nijedna od ovih dorada nije zatvorena samim pisanjem revizije.

## Implementirani funkcionalni opseg i ciljne provjere

Izvor: službeni Redmine vodič i dokumentacija, pregledani 2026-11-08:
- https://www.redmine.org/projects/redmine/wiki/Guide
- https://www.redmine.org/projects/redmine/wiki/Features
- https://www.redmine.org/projects/redmine/wiki/RedmineRoles
- https://www.redmine.org/projects/redmine/wiki/RedmineIssueTrackingSetup
- https://www.redmine.org/projects/redmine/wiki/RedmineIssues
- https://www.redmine.org/projects/redmine/wiki/RedmineTimeTracking

Ovo je vlastita .NET implementacija funkcionalnog uzora, ne kopija izvornog koda. Popis obuhvaća glavne module i proširenja iz službenog vodiča. Status „gotovo” znači implementirano i provjereno za opisani opseg; ne znači potpunu ekvivalentnost Redmineu.

| ID | Cjelina / kriterij prihvata | Status |
|---|---|---|
| AUTH-01 | Prijava brojem radnika, hash lozinke, odjava, privremeno zaključavanje | Gotovo |
| AUTH-02 | Prvi admin/admin samo u praznoj bazi, neobvezni podsjetnik za promjenu | Gotovo |
| AUTH-03 | Profil, funkcija, organizacijska jedinica, promjena lozinke | Gotovo |
| AUTH-04 | Deaktivacija, reset lozinke, opoziv aktivnih sesija | Gotovo |
| AUTH-05 | Registracija s odobrenjem / potvrdom e-pošte | Gotovo: opcionalna registracija s administratorskim odobrenjem, HR/EN; potvrda e-pošte izvan ovog opsega |
| AUTH-06 | AD/LDAP i autentikacija kroz bankarski SSO | Opcionalni OIDC SSO i LDAPS, HR/EN, provjereni lokalno s Keycloakom i OpenLDAP-om. Ovlasti ostaju na postojećim Team Manager računima. Preostaje provjera s bankarskim pružateljem identiteta i Active Directoryjem |
| ROLE-01 | Prilagodljive role, zbroj globalnih ovlasti, serverske provjere | Gotovo |
| ROLE-02 | Članstva po timu/projektu, izolacija ovlasti između projekata | Gotovo |
| ROLE-03 | Više rola po članstvu, grupe korisnika i nasljeđivanje | Gotovo: više rola po članstvu, grupe s globalnim rolama i opcionalno nasljeđivanje projektnih rola |
| ROLE-04 | Pravila vidljivosti privatnih zadataka i pojedinih polja | Gotovo: privatni zadaci i ograničavanje vidljivosti prilagodljivih polja zadataka; HR/EN sučelje |
| ORG-01 | Unos organizacijskih jedinica, timova i projekata | Gotovo |
| PROJ-01 | Pregled projekata i pripadajućih zadataka prema ovlastima | Gotovo |
| PROJ-02 | Hijerarhija, opis, zatvaranje/arhiviranje, privatni/javni projekti | Gotovo: javno prijavljenim korisnicima; stanje po projektu |
| PROJ-03 | Uključivanje modula i postavke po projektu | Gotovo za postojeće module: zadaci/komentari i vrijeme; novi moduli dodaju se s vlastitim stavkama |
| ISSUE-01 | Unos i uređivanje zadataka, tip, prioritet, status, rok, nositelj | Gotovo |
| ISSUE-02 | Komentari i transakcijska povijest promjena | Gotovo: povijest s detaljima prije/poslije |
| ISSUE-03 | Filtri po projektu/statusu/tekstu | Gotovo |
| ISSUE-04 | Prilagodljivi tipovi, statusi i prijelazi prema roli i tipu | Gotovo: dodavanje šifrarnika, dodavanje/uklanjanje prijelaza; početni status Novo |
| ISSUE-05 | Podzadaci, veze, ovisnosti, duplikati | Gotovo: veze na detaljima zadatka, jedan nadređeni zadatak, provjera ciklusa; ovisnosti služe za pregled |
| ISSUE-06 | Skupno uređivanje, kopiranje/premještanje i CSV izvoz | Gotovo: CSV, skupna izmjena statusa/prioriteta, kopiranje i premještanje pojedinačnog zadatka |
| ISSUE-07 | Promatrači, privatne bilješke i obavijesti | Gotovo: osobno praćenje, bilješke vidljive autoru i obavijesti unutar aplikacije |
| QUERY-01 | Spremljeni upiti, stupci, grupiranje, sortiranje i sažeci | Gotovo: osobni upiti spremaju filtre, stupce, sortiranje i grupiranje; broj zadataka po grupi i ukupno |
| PLAN-01 | Verzije, prekretnice i roadmap | Gotovo: verzije i prekretnice, povezivanje zadataka i napredak prema statusima |
| PLAN-02 | Kalendar i Gantt s datumima i ovisnostima | Gotovo: mjesečni kalendar rokova i Gantt s datumima i ovisnostima između prikazanih zadataka |
| TIME-01 | Unos sati po zadatku i pregled zapisa | Gotovo |
| TIME-02 | Izvještaji prema osobi, projektu, aktivnosti i razdoblju | Gotovo |
| FIELD-01 | Korisnička polja za zadatke, korisnike, projekte i vrijeme | Gotovo: tekstualna polja za zadatke, korisnike, projekte i unose vremena; HR/EN sučelje |
| DOC-01 | Dokumenti, datoteke i kontrolirani prilozi | Gotovo: projektna zbirka dokumenata i datoteka te kontrolirani prilozi na zadacima |
| WIKI-01 | Wiki po projektu, verzije, usporedba i prava uređivanja | Gotovo: tekstualne stranice, povijest i usporedba dviju verzija uz projektne ovlasti |
| NEWS-01 | Novosti i komentari | Gotovo: projektne objave i komentari s projektnim ovlastima |
| FORUM-01 | Forumi, teme, odgovori i moderiranje | Gotovo: jedan forum po projektu, teme i odgovori, zaključavanje i otključavanje tema |
| SCM-01 | Veze s Git repozitorijima, pregled commitova i razlika | Gotovo: HTTPS poveznice i HR/EN pregled commitova i razlika iz konfiguriranih lokalnih Git repozitorija |
| NOTIFY-01 | SMTP obavijesti, podsjetnici, red slanja i ponavljanje | Gotovo: SMTP za praćene zadatke i podsjetnike na dan roka, trajni red i ponovni pokušaji; HR/EN |
| MAIL-01 | Zaprimanje poruka e-pošte u zadatke | Djelomično: autentificirani prihvat poruka iz pristupnika, deduplikacija i HR/EN sučelje; dodan TLS IMAP konektor; preostaje povezivanje i provjera namjenskog sandučića |
| FEED-01 | Feed aktivnosti i Atom/RSS | Gotovo: zadnjih 50 aktivnosti zadataka, projektni filtar, Atom/RSS uz prijavu |
| SEARCH-01 | Pretraživanje kroz module i osobna nadzorna ploča | Gotovo: osobni pregled otvorenih zadataka i rokova te pretraga projekata, zadataka, verzija, prekretnica i korisnika |
| API-01 | Dokumentirani REST API i pristupni tokeni | Gotovo: verzionirani API za čitanje projekata i zadataka, osobni tokeni i HR/EN sučelje |
| EXT-01 | Proširenja, teme i lokalizacija | Gotovo: teme po računu, HR/EN sučelje implementiranih modula i proširenja projektne navigacije |
| AUDIT-01 | Revizijski zapisi administracije i promjena ovlasti | Gotovo: računi, uloge i ovlasti, grupe, članstva, organizacija, postavke projekata, tijek rada i karta planova |
| OPS-01 | Verzijske SQL skripte, ponovljiva instalacija i odvojeni demo podaci | Gotovo, provjereno na zasebnim bazama |
| OPS-02 | Konfiguracija JSON/env, Windows publish paket i upute za IIS | Aktualni Windows paket ponovno provjeren na lokalnom IIS-u 25. 11. 2026.: HTTPS, namjenski pool/SQL baza, idempotentna instalacija i HR/EN tokovi; čeka ciljni IIS |
| OPS-03 | Backup/restore, rollback, nadzor, zdravstvene provjere i logovi | Aktualna nadogradnja čuva konfiguraciju/podatke uz COPY_ONLY/CHECKSUM backup, VERIFYONLY i health/hash provjeru. Obnova i rollback ranije lokalno provjereni; nisu ponovno izvršeni u ovom krugu. Ciljni povratak i nadzor ostaju otvoreni |
| OPS-04 | OCP manifesti i testiranje iza proxyja | Predlošci obrađeni s `oc process`; lokalno provjeren HTTPS proxy. Linux OCI slika izgrađena i provjerena u Podmanu s odvojenom SQL bazom, migracijama, neprivilegiranim UID-om, HTTPS prijavom i sesijom nakon restarta. Čeka ciljni OCP klaster |
| DB-01 | SQL Server | Gotovo |
| DB-02 | Oracle i Db2 LUW/zOS provider, migracije i integracijski testovi | Lokalno provjereni Oracle demo i transakcijski uvoz projekata, zadataka, članstava i njihovih uloga, prekretnica, veza, povijesti i vremena. Aktivni obrasci projekata i zadataka, prijava i ovlasti sada koriste 51 tipiziranu relacijsku Oracle tablicu, uz ključeve, transakcijski prijenos i očuvanje povezanih podataka. JSON ostaje zaštićena arhiva. Lokalno provjereni migracija/rollback, ovlasti i HR/EN. Popisi/detalji projekata i zadataka te efektivne ovlasti koriste ciljane parametrizirane SQL upite; izmjene dohvaćaju potrebne retke bez učitavanja radne cjeline. Provjereni filtri, izvori prava i opseg čitanja. DBMS_LOCK omogućuje paralelne upise različitih zadataka uz izolaciju promjena prava; sekvence uklanjaju zajedničke brojače. SQL agregirani/ograničeni izvještaji vremena, aktivnosti, obavijesti, napretka plana i revizije lokalno provjereni. Administracija računa, članstava i dodjela uloga zaključava korisnike, plan se zaključava po projektu; očuvani actor/assignee dependency lockovi i zaštita zadnjeg administratora. Lokalno provjereni paralelna administracija i opozivi tijekom čekanja. Uloge, šifrarnici i tijek rada zaključavaju se po ovisnosti, hijerarhija/moduli po projektu i pretcima uz ponovno otkrivanje promijenjene putanje. Provjereni neovisne grane, opozivi kataloških prava i zaštita od ciklusa. Novi projekti rezerviraju i zaključavaju ID/naziv/roditeljsku putanju; veze zadataka zaključavaju krajeve i vrstu grafa, polja/upiti/obavijesti vlastite entitete. Redovni upisi nemaju globalni isključivi fallback; testirani paralelno stvaranje, dupli nazivi, pogađanje ID-ja, ciklusi i opozivi. Dohvati i sekvence u callbackovima sada su asinkroni, uz iste transakcije, ovlasti i zaključavanja. Projektne novosti i komentari sada rade na Oracleu: ciljani popis/detalji, očuvani autori i veze, naslijeđene ovlasti, revalidacija upisa pod zaključavanjem te HR/EN validacija. Projektni dokumenti i datoteke rade na Oracleu: popis metapodataka bez BLOB-a, autorizirano preuzimanje, dodavanje i brisanje uz provjeru prava nakon čekanja, očuvani autori i binarni sadržaj te HR/EN validacija. Projektni wiki radi na Oracleu uz ciljane popise, povijest i usporedbu verzija, atomsko spremanje stranice/revizije, zaštitu od zastarjelih upisa i duplikata te HR/EN poruke. Provjereni naslijeđene ovlasti, opoziv tijekom čekanja i očuvanje povijesti. Projektni forum radi na Oracleu uz ciljane teme/odgovore, očuvane autore, odvojena prava objavljivanja i moderiranja, transakcijsko zaključavanje teme i HR/EN validaciju/status. Provjereni opozivi tijekom čekanja i paralelni rad drugih tema. Projektne Git poveznice rade na Oracleu uz ciljane autorizirane dohvaćaje, transakcijski audit, zaštitu duplikata i provjeru prava nakon čekanja. Lokalno provjereni konfigurirani Git commitovi/razlike i HR/EN. Ulazna e-pošta radi na Oracleu: atomski privatni zadatak i potvrda poruke, deduplikacija po projektu, gateway i TLS IMAP, provjere prava nakon čekanja i HR/EN prikaz. Osobni API tokeni i REST čitanje rade na Oracleu, uz hash tokena, vlasništvo, istek, opoziv i poništavanje pri promjeni sesije. Registracija i administratorsko odobravanje rade na Oracleu, uz zaštitu duplikata/konflikata, očuvanje hashova lozinki i transakcijski audit. Grupe rade na Oracleu: članstva, globalne uloge, audit i zaštita zadnjeg administratora. Protokol zaključavanja 5 štiti grupne ovisnosti i ponovno otkriva promjene članstva bez globalnog blokiranja upisa. SMTP red, leaseovi, ponovni pokušaji, podsjetnici i osobne postavke rade na Oracleu. Lokalno provjereni stvarna HR/EN SMTP isporuka, odgovor 451, zaštita stale leasea i deduplikacija podsjetnika. Db2 LUW 12.1.5 lokalno je dovršen: službeni .NET 10 driver, migracija 51 tablice i neovisna usporedba 10.465 zapisa, zamrznuta arhiva, ciljani SQL, sekvence i aplikacijski lockovi, paralelni upisi i opozivi grupnih prava, HR/EN SMTP s ponavljanjem i podsjetnikom. Windows E2E i 52 HR/EN WCAG skeniranja prošli; Linux slika radi kao UID 1654 iza HTTPS proxyja i čuva sesiju nakon restarta. SQL Server i Oracle regresije prošle. Preostaju Db2 z/OS s odgovarajućom licencom i ciljne produkcijske provjere |
| QA-01 | Integracijske provjere ovlasti, bootstrapa i novih modula | Lokalni paket provjera na privremenim bazama: bootstrap, ovlasti i moduli, API i Git, gateway e-pošte te lokalni SMTP i TLS IMAP. Provjeravaju se i granica naziva tipa (50 znakova) te odvojeno demo učitavanje u Windows paketu |
| QA-02 | Browser E2E, pristupačnost, opterećenje i produkcijski pregled | Ponovljeno 25. 11. 2026. na aktualnom lokalnom IIS-u: HR/EN Edge E2E, ovlasti, axe, ručni NVDA i 24 stvarne govorne provjere, 2710 odgovora bez HTTP grešaka, p95 301 ms pri 25 paralelnih zahtjeva. Opseg/mjerenja: LOCAL-IIS-NVDA-2026-11-25.md. Ciljni produkcijski kapacitet i konfiguracija ostaju za provjeru |

## Pravilo isporuke

Svaka cjelina: migracija bez gubitka podataka, serverska autorizacija, validacija, demo scenarij, integracijski test, dokumentiranje ograničenja. Produkcijskim spremnim smatramo tek izdanje koje prođe i QA-02, AUTH-04 i OPS-03. Preostale stavke nisu automatski završene samim objavljivanjem izvornog koda na GitHubu.
