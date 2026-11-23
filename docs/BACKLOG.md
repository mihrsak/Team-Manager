# Backlog Team Manager

## Druga revizija — status dorada 23. 11. 2026.

Pregled demo okruženja na Db2-u, 44 HR/EN prikaza, tri ograničene uloge i 24 axe provjere. Pronađeno je deset nalaza; REV2-01 i REV2-02 sada su dovršeni i provjereni na SQL Serveru, Oracleu i Db2, HR/EN. Otvoreno ostaje osam stavki (pet P2 i tri P3). Dokazi, reprodukcije, vrste nalaza i kriteriji prihvata: [PRODUCT-REVIEW-2026-11-23.md](PRODUCT-REVIEW-2026-11-23.md). Početni pregled bio je na Db2-u; naknadne SQL Server/Oracle provjere obuhvaćaju navedene P1 popravke, a ne cijelo produkcijsko okruženje.

| Stavka | Prioritet | Status / predložena dorada |
|---|---|---|
| REV2-01 | P1 | Dovršeno: sačuvani sirovi nacrt i izvorna wiki revizija, izričita potvrda nakon pregleda aktualne verzije, zaštita ponovnog konflikta; vraćena polja nevaljanog vremena i upozorenje pri odlasku s vraćenog nacrta. HR/EN, sva tri providera. |
| REV2-02 | P1 | Dovršeno: dokumenti/privitci koriste download poveznice; CSV se preuzima u odvojenom skrivenom okviru bez navigacije uređivane stranice; nove izmjene i povratak iz povijesti ponovno aktiviraju zaštitu. HR/EN stvarna preuzimanja, odustanak od reloada i pojedinačna potvrda odlaska, sva tri providera. |
| REV2-03 | P2 | Otvoreno: ograničiti vodoravno pomicanje tablice vremena na detalju zadatka; cijela stranica sada prelazi 320/390 px. |
| REV2-04 | P2 | Otvoreno: lokalizirati serverske Required/DataAnnotations poruke i nazive polja u HR/EN. |
| REV2-05 | P2 | Otvoreno: zadržati siguran povratni kontekst iz pretrage i izvještaja vremena, uz aktivnu navigaciju povezanih stranica. |
| REV2-06 | P2 | Otvoreno: uskladiti Gantt opis raspona s konfiguriranom kulturom datuma. |
| REV2-07 | P2 | Otvoreno: na detalju vremena prikazati autora, zadatak i projekt prije ispravka. |
| REV2-08 | P3 | Prijedlog: čitljive promjene polja u povijesti, uz očuvan izvorni JSON zapis. |
| REV2-09 | P3 | Prijedlog: generički HR/EN 404 prikaz s povratkom u aplikaciju, bez otkrivanja privatnih zapisa. |
| REV2-10 | P3 | Otvoreno: omogućiti povratak s prazne kasnije stranice pretrage. |

Sljedeće: REV2-03/04/06, povezani tokovi REV2-05/07 te REV2-08–10. Regresija P1 popravaka: scripts/Test-DraftProtection.cjs na izoliranim testnim bazama; glavni demo i postojeći radni Db2 pregled nadograđeni su bez promjene konfiguracije ili migracije baze. Poslovni podaci demoa nisu mijenjani radi stvaranja regresijskih primjera; jezik i tema administratora vraćeni su na početne postavke.

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
| OPS-02 | Konfiguracija JSON/env, Windows publish paket i upute za IIS | Provjereno na lokalnom IIS-u: Windows paket, HTTPS, izdvojeni application pool i SQL Express baza; čeka provjeru na ciljnom IIS-u |
| OPS-03 | Backup/restore, rollback, nadzor, zdravstvene provjere i logovi | Na lokalnom IIS-u provjereni liveness i readiness, nadogradnja, obnova baze i povratak prethodnog paketa uz prijavu i prikaz obnovljenog projekta; čeka povratak i nadzor na ciljnom IIS-u |
| OPS-04 | OCP manifesti i testiranje iza proxyja | Predlošci obrađeni s `oc process`; lokalno provjeren HTTPS proxy. Linux OCI slika izgrađena i provjerena u Podmanu s odvojenom SQL bazom, migracijama, neprivilegiranim UID-om, HTTPS prijavom i sesijom nakon restarta. Čeka ciljni OCP klaster |
| DB-01 | SQL Server | Gotovo |
| DB-02 | Oracle i Db2 LUW/zOS provider, migracije i integracijski testovi | Lokalno provjereni Oracle demo i transakcijski uvoz projekata, zadataka, članstava i njihovih uloga, prekretnica, veza, povijesti i vremena. Aktivni obrasci projekata i zadataka, prijava i ovlasti sada koriste 51 tipiziranu relacijsku Oracle tablicu, uz ključeve, transakcijski prijenos i očuvanje povezanih podataka. JSON ostaje zaštićena arhiva. Lokalno provjereni migracija/rollback, ovlasti i HR/EN. Popisi/detalji projekata i zadataka te efektivne ovlasti koriste ciljane parametrizirane SQL upite; izmjene dohvaćaju potrebne retke bez učitavanja radne cjeline. Provjereni filtri, izvori prava i opseg čitanja. DBMS_LOCK omogućuje paralelne upise različitih zadataka uz izolaciju promjena prava; sekvence uklanjaju zajedničke brojače. SQL agregirani/ograničeni izvještaji vremena, aktivnosti, obavijesti, napretka plana i revizije lokalno provjereni. Administracija računa, članstava i dodjela uloga zaključava korisnike, plan se zaključava po projektu; očuvani actor/assignee dependency lockovi i zaštita zadnjeg administratora. Lokalno provjereni paralelna administracija i opozivi tijekom čekanja. Uloge, šifrarnici i tijek rada zaključavaju se po ovisnosti, hijerarhija/moduli po projektu i pretcima uz ponovno otkrivanje promijenjene putanje. Provjereni neovisne grane, opozivi kataloških prava i zaštita od ciklusa. Novi projekti rezerviraju i zaključavaju ID/naziv/roditeljsku putanju; veze zadataka zaključavaju krajeve i vrstu grafa, polja/upiti/obavijesti vlastite entitete. Redovni upisi nemaju globalni isključivi fallback; testirani paralelno stvaranje, dupli nazivi, pogađanje ID-ja, ciklusi i opozivi. Dohvati i sekvence u callbackovima sada su asinkroni, uz iste transakcije, ovlasti i zaključavanja. Projektne novosti i komentari sada rade na Oracleu: ciljani popis/detalji, očuvani autori i veze, naslijeđene ovlasti, revalidacija upisa pod zaključavanjem te HR/EN validacija. Projektni dokumenti i datoteke rade na Oracleu: popis metapodataka bez BLOB-a, autorizirano preuzimanje, dodavanje i brisanje uz provjeru prava nakon čekanja, očuvani autori i binarni sadržaj te HR/EN validacija. Projektni wiki radi na Oracleu uz ciljane popise, povijest i usporedbu verzija, atomsko spremanje stranice/revizije, zaštitu od zastarjelih upisa i duplikata te HR/EN poruke. Provjereni naslijeđene ovlasti, opoziv tijekom čekanja i očuvanje povijesti. Projektni forum radi na Oracleu uz ciljane teme/odgovore, očuvane autore, odvojena prava objavljivanja i moderiranja, transakcijsko zaključavanje teme i HR/EN validaciju/status. Provjereni opozivi tijekom čekanja i paralelni rad drugih tema. Projektne Git poveznice rade na Oracleu uz ciljane autorizirane dohvaćaje, transakcijski audit, zaštitu duplikata i provjeru prava nakon čekanja. Lokalno provjereni konfigurirani Git commitovi/razlike i HR/EN. Ulazna e-pošta radi na Oracleu: atomski privatni zadatak i potvrda poruke, deduplikacija po projektu, gateway i TLS IMAP, provjere prava nakon čekanja i HR/EN prikaz. Osobni API tokeni i REST čitanje rade na Oracleu, uz hash tokena, vlasništvo, istek, opoziv i poništavanje pri promjeni sesije. Registracija i administratorsko odobravanje rade na Oracleu, uz zaštitu duplikata/konflikata, očuvanje hashova lozinki i transakcijski audit. Grupe rade na Oracleu: članstva, globalne uloge, audit i zaštita zadnjeg administratora. Protokol zaključavanja 5 štiti grupne ovisnosti i ponovno otkriva promjene članstva bez globalnog blokiranja upisa. SMTP red, leaseovi, ponovni pokušaji, podsjetnici i osobne postavke rade na Oracleu. Lokalno provjereni stvarna HR/EN SMTP isporuka, odgovor 451, zaštita stale leasea i deduplikacija podsjetnika. Db2 LUW 12.1.5 lokalno je dovršen: službeni .NET 10 driver, migracija 51 tablice i neovisna usporedba 10.465 zapisa, zamrznuta arhiva, ciljani SQL, sekvence i aplikacijski lockovi, paralelni upisi i opozivi grupnih prava, HR/EN SMTP s ponavljanjem i podsjetnikom. Windows E2E i 52 HR/EN WCAG skeniranja prošli; Linux slika radi kao UID 1654 iza HTTPS proxyja i čuva sesiju nakon restarta. SQL Server i Oracle regresije prošle. Preostaju Db2 z/OS s odgovarajućom licencom i ciljne produkcijske provjere |
| QA-01 | Integracijske provjere ovlasti, bootstrapa i novih modula | Lokalni paket provjera na privremenim bazama: bootstrap, ovlasti i moduli, API i Git, gateway e-pošte te lokalni SMTP i TLS IMAP. Provjeravaju se i granica naziva tipa (50 znakova) te odvojeno demo učitavanje u Windows paketu |
| QA-02 | Browser E2E, pristupačnost, opterećenje i produkcijski pregled | Lokalni IIS: Edge E2E i ovlasti, 18 automatiziranih WCAG A/AA provjera, ručni pregled i NVDA najave glavnih tokova u HR/EN, kontrola IIS konfiguracije te 34.377 zahtjeva bez grešaka. Na 25 paralelnih zahtjeva p95 22 ms. Kapacitet i konfiguracija na ciljnom produkcijskom IIS-u ostaju za provjeru |

## Pravilo isporuke

Svaka cjelina: migracija bez gubitka podataka, serverska autorizacija, validacija, demo scenarij, integracijski test, dokumentiranje ograničenja. Produkcijskim spremnim smatramo tek izdanje koje prođe i QA-02, AUTH-04 i OPS-03. Preostale stavke nisu automatski završene samim objavljivanjem izvornog koda na GitHubu.
