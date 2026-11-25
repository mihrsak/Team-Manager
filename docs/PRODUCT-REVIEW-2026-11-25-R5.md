# Peta revizija Team Managera — 25. 11. 2026.

Pregledan je commit **ed45f22**, paket `artifacts/rev4-export-titles-catalog`, na [lokalnom Db2 demou](https://localhost:7610). Potvrđeno je **pet preostalih nedostataka: jedan P1, dva P2 i dva P3**. Ovaj korak donosi pregled i backlog, bez implementacije novih dorada.

## Opseg i rezultati

- **82 HR/EN prikaza**: po 41 na svakom jeziku, desktop 1440 px i mobilnih 320 px. Projekti, zadaci i novi zadatak, grupiranje, pretraga, kalendar/Gantt, aktivnosti, planovi, wiki, dokumenti, novosti, forum, repozitoriji, izvještaj/detalji vremena, obavijesti, audit, workflow, korisnici/profili, ovlasti, grupe, registracijski zahtjevi, polja i njihove definicije, osobne postavke/tokeni, ulazna pošta i stranice grešaka. Uključena je početna promjena administratorske lozinke.
- Svih **82 axe provjere** WCAG 2 A/AA i 2.1 A/AA prolaze bez prijavljenih povreda. Nema prelijevanja cijelog dokumenta na 320 px ni JavaScript `pageerror` događaja. Vizualno su pregledani mobilni izvještaj vremena i prikaz sukoba polja; automatska provjera ne zamjenjuje potpun ručni pregled pristupačnosti ili NVDA.
- Voditelj i razvojni korisnik imaju dopušten pristup privatnom projektu; promatrač dobiva 404 za privatni projekt, zadatak i izvještaj, ne vidi privatno članstvo kroz tuđi profil i ne može uređivati dopušteni zadatak. Administrativne stranice za te korisnike završavaju na odbijenom pristupu. Nema novog otkrivanja privatnog članstva u tim provjerama.
- Završni `Test-ProductDemo.cjs` prolazi, uključujući dodatnih **28 HR/EN axe provjera**, demo sadržaj i provjerene granice privatnosti. Poslovni podaci demoa ostaju sačuvani; odbijeni veliki prijenosi nisu dodali dokument. Jezik i tema administratora vraćeni su na početne postavke.
- U privremenoj SQL Server bazi provjereni su stvarno stvaranje projekta/zadataka, evidencija vremena i definicije/vrijednosti polja, zatim osam sukoba polja, dva odbijena skupna uređivanja i dvije neuspjele registracijske predaje, HR/EN. Generirana baza i njezin poslužitelj uklonjeni su završetkom testnog runnera. Ti upisi nisu provedeni na čistom demou.
- Ponovljeni `Test-DocumentDraftsFilters.cjs` prolazi na aktualnom paketu u zasebnoj privremenoj SQL bazi: prethodni nacrti dokumenata, naslov/veličina/nedostajuća datoteka, početni prazni retci, zaštita odlaska/reloada, ispravljena predaja, nevaljani i ponovljeni filtri, spremljeni upiti/CSV/skupna radnja te 404 nakon opoziva. Novi sukob skupnog uređivanja nije bio dio te ranije regresije.

Prethodno zatvoreni nalazi REV4 ostaju zatvoreni za ranije provjereni opseg. Novi scenarij prijenosa prelazi HTTP limit poslužitelja, dok je raniji test prelazio samo aplikacijski limit od 10 MiB. Novi sukobi odnose se na prilagodljiva polja i skupno uređivanje, izvan ranijih popravaka nacrta foruma, planova, novosti i dokumenata.

Ovo je svježi pregled Db2 demoa uz izolirane SQL Server provjere. Nije nova potvrda Oraclea, ciljnog IIS-a/OpenShifta, NVDA, kapaciteta, oporavka baze ili bankarskih integracija. Novi nalazi u zajedničkim PageModelima zahtijevaju regresije na sva tri providera tijekom implementacije. Git pregled bez povezanog demo repozitorija nije potvrđen. Hrvatski native tekstovi Windowsa/Edgea i namjerno dvojezični korisnički podaci nisu tretirani kao pogreške prijevoda aplikacije.

## Predloženi redoslijed

| ID | Prioritet | Potvrđeni nedostatak | Predložena dorada |
|---|---|---|---|
| REV5-01 | **P1** | Sukob briše nacrt prilagodljivog polja i vraća novu reviziju. | Sačuvati izvorni unos i reviziju, odvojiti aktualnu vrijednost od nacrta te tražiti pregled prije ponovne predaje. |
| REV5-02 | **P2** | Odbijeno skupno uređivanje briše odabir zadataka i željeni prioritet. | Očuvati namjeru radnje uz jasan prikaz sukoba i izričit pregled aktualnih revizija. |
| REV5-03 | **P2** | Prijenos iznad HTTP limita završava generičkom HTTP 400 stranicom. | Prije slanja provjeriti veličinu, dati HR/EN uputu i siguran oporavak uz očuvanu serversku zaštitu. |
| REV5-04 | **P3** | Pogrešna potvrda lozinke briše ime i broj radnika u registraciji. | Sačuvati nesenzitivna polja i fokusirati razumljivu grešku; lozinke ponovno unijeti. |
| REV5-05 | **P3** | Registracija i početna promjena lozinke imaju generički naslov kartice. | Dodati opisne HR/EN naslove za oba dostupna toka. |

## Reprodukcije i kriteriji prihvata

### REV5-01 — nacrti prilagodljivih polja

Na `/CustomFields?scope=project&entityId=...` urednik A upisuje vrijednost s početnim/završnim razmacima. Urednik B sprema drugu vrijednost s istom početnom revizijom 0. A šalje izvorni obrazac. Serverska zaštita odbija zastarjelu predaju, ali prikaz zamjenjuje A-ov tekst B-ovom vrijednošću, a reviziju 0 revizijom 1. Nema oznake vraćenog nacrta ni aktivne zaštite odlaska. Isti rezultat potvrđen je za `scope=user` i `scope=time`, HR/EN.

Na zadatku A uređuje prilagodljivo polje; B u međuvremenu sprema naslov zadatka. A-ova predaja polja odbijena je uz lokaliziranu poruku, ali polje postaje prazno i obrazac dobiva aktualnu reviziju zadatka. Potvrđeno na oba jezika. Pohranjena B-ova izmjena nije prepisana u toj predaji; problem je gubitak A-ova nacrta i nejasan ponovni pokušaj.

Izvor: `src/TeamManager.Web/Pages/CustomFields.cshtml.cs:49`, `src/TeamManager.Web/Pages/Issue.cshtml.cs:227`. Obrada greške ponovno učitava model bez zasebnog vraćenog nacrta.

Prihvat: sačuvati sirovi unos, identitet polja i izvornu reviziju; jasno prikazati nacrt i aktualnu vrijednost; očuvati upozorenje pri odlasku/reloadu; novo preuzimanje revizije zahtijeva izričit pregled, uključujući drugi sukob. Provjeriti HR/EN, SQL Server/Oracle/Db2, opoziv prava i izostanak otkrivanja nedostupnih vrijednosti. Uspješna predaja treba ukloniti oznaku nacrta.

### REV5-02 — skupna radnja nakon sukoba

Na filtriranom `/Issues` odabrati dva zadatka i prioritet `Visok`. Drugi urednik promijeni jedan zadatak. Poslati ranije otvoren skupni obrazac: pojavljuje se lokalizirana poruka o sukobu, ali oba odabira nestaju, a prioritet se vraća na prazno, odnosno zadržavanje postojećeg. Potvrđeno HR/EN. Ova provjera potvrđuje odbijenu predaju i gubitak odabira; nije zaseban dokaz svih mogućih transakcijskih rollback scenarija.

Izvor: `src/TeamManager.Web/Pages/Issues.cshtml.cs:180` i skupni obrazac u `Issues.cshtml`.

Prihvat: nakon greške prikazati izvorni dopušteni skup i željene vrijednosti, zadržati filtre, razlikovati promijenjene/nedostupne zadatke i ponuditi izričitu obnovu nakon pregleda. Ne preuzimati nove revizije za tihu ponovnu predaju; potvrditi atomski upis, ponovljeni sukob i opoziv prava na sva tri providera.

### REV5-03 — prevelika datoteka prije Page handlera

Na `/ProjectDocuments?projectId=1` unijeti naslov i opis, odabrati `.txt` od **32 MiB (33.554.432 B)** i poslati. Umjesto aplikacijske greške prikazuje se prazni HTTP **400**, koji Edge zamjenjuje vlastitom stranicom greške. Serverski log potvrđuje `Request body too large`, s HTTP limitom **30.000.000 B**. Aplikacija dopušta najviše 10 MiB; odbijena datoteka nije spremljena. Isto je potvrđeno u HR i EN.

U provjerenom Edgeu **Povratak vraća naslov i opis**. Zato ovaj nalaz nije potvrda nepovratnog gubitka teksta. Problem je prekid toka bez aplikacijske upute, bez prikazanog nacrta na odgovoru i oslanjanje na ponašanje povijesti preglednika. Jezik Edgeove sistemske greške pripada Windowsu, a ne lokalizaciji aplikacije.

Izvor: obrazac `src/TeamManager.Web/Pages/ProjectDocuments.cshtml` i aplikacijska provjera u `UploadedFile.cs`; HTTP odbijanje nastaje prije uobičajene obrade nacrta.

Prihvat: s JS-om odbiti preveliku datoteku prije slanja uz HR/EN poruku, sačuvan tekst i zaštitu izmjena. Server/proxy moraju zadržati ograničenja i razumljiv oporavak za prevelik zahtjev bez JS-a; ne povećavati limit bez opravdanja. Provjeriti granice 10 MiB i HTTP/proxy limita, broj dokumenata te normalan prijenos nakon korekcije. Ne obećavati vraćanje odabira datoteke.

### REV5-04 — registracijski obrazac nakon greške

U uključenu registraciju anonimno unijeti valjan broj radnika i ime, zatim dvije različite lozinke koje svaka zadovoljavaju minimalnu duljinu. Serverska validacija daje prevedenu poruku, ali ime i broj radnika postaju prazni. Potvrđeno HR/EN u izoliranom SQL okruženju s uključenom registracijom, bez slanja valjanog registracijskog zahtjeva.

Izvor: `src/TeamManager.Web/Pages/Register.cshtml.cs:19` i `Register.cshtml`. Lozinke se očekivano ne vraćaju u HTML.

Prihvat: kod odbijene predaje sačuvati sigurno kodiran broj radnika i ime, lozinke ostaviti praznima, dati jasnu HR/EN uputu i prikladan fokus. Očuvati isključivanje registracije, rate limiting i neotkrivanje postojanja računa.

### REV5-05 — naslovi preostalih početnih stranica

`/InitialPassword` na demou i `/Register` u uključenom izoliranom okruženju imaju naslov **`Team Manager · Team Manager`**, na oba jezika, iako je H1 opisni i preveden. `PageLabels.For` nema njihove oznake. To nije ponavljanje prethodno zatvorenih naslova administrativnih zahtjeva i kataloga.

Prihvat: oba toka imaju različite, opisne HR/EN naslove, sa i bez JS-a; ne uključivati osobne podatke ili tajne. Ne mijenjati ponašanje onemogućene registracije ili preusmjeravanja prijavljenog korisnika.

## Dokazi

Lokalno, ignorirano: `artifacts/product-review5-2026-11-25/overview.json`, `isolated.json`, `oversized.json`, HR/EN tekstualne snimke i PNG reprodukcije. Pokretači: `artifacts/ReviewProduct5.cjs`, `Review5Isolated.cjs` preko `scripts/Test-AccountSql.ps1`, `Review5Oversized.cjs` te postojeći `scripts/Test-ProductDemo.cjs`. Testne lozinke i pristupne konfiguracije nisu dio izvještaja ili repozitorija.

Preporuka: prvo REV5-01, zatim REV5-02 i REV5-03, potom REV5-04/05 zajedno kao dorada početnih tokova. Nakon provedbe ponoviti iste reprodukcije i ciljane regresije; vanjske produkcijske provjere ostaju zasebne.
