# Četvrta revizija Team Managera — 24. 11. 2026.

Pregledani commit: `aed6e3d` (implementacija `10c8943`), paket `artifacts/rev3-navigation-complete`, [demo](https://localhost:7610), Db2 LUW, shema 11 / protokol 5. Postojeća aplikacija i baza ostale su dostupne tijekom pregleda. Izvještaj dokumentira **pet nalaza: jedan P1, jedan P2 i tri P3**; prilikom izvornog pregleda popravci novih nalaza nisu bili implementirani.

Najviši prioritet je očuvanje tekstualnog nacrta dokumenta nakon odbijenog prijenosa. Prethodne dorade REV3-01–06 prolaze svoje provjere; novi nalazi obuhvaćaju dodatne obrasce, nevaljane filtre, izvoz i administrativni kontekst izvan prethodnih kriterija prihvata. Raniji lokalni prihvat ostaje dokaz za tada izričito provjereni opseg.

Naknadni status 25. 11. 2026.: **Svih pet nalaza REV4-01–REV4-05 popravljeno je i provjereno na SQL Serveru, Oracleu i Db2, HR/EN**. Aktualni paket je `artifacts/rev4-export-titles-catalog`; svih pet nalaza lokalno je zatvoreno. Izvorni nalazi ispod ostaju dokaz pregledanog commita; detalji provjera su u [RELEASE-STATUS.md](RELEASE-STATUS.md).

## Što je provjereno

- **80 HR/EN prikaza**, po 40 stranica na svakom jeziku: glavni projekti/zadaci i novi zadatak, grupirani popis, pretraga i prazna kasnija stranica, kalendar/Gantt, aktivnosti, planovi, wiki, dokumenti, novosti, forum, repozitoriji, vrijeme, obavijesti, audit, workflow, korisnici/profil, osobne postavke/tokeni, role, grupe, zahtjevi za registraciju te polja projekta/korisnika/vremena i njihov katalog.
- Desktop 1440 px i mobilnih 320 px u početnoj svijetloj temi, **80 axe provjera** WCAG 2 A/AA i 2.1 A/AA. U tim provjerama nema prijavljenih povreda, prelijevanja cijelog dokumenta ni JavaScript `pageerror` događaja. Provjera značenja naslova stranice ostaje ručna; generički naslov može proći axe.
- Voditelj i razvojni korisnik vide privatni projekt, a promatrač dobiva 404 za privatni projekt/zadatak/izvještaj i ne vidi privatno članstvo kroz tuđi profil. Na dopuštenom zadatku promatraču je uređivanje onemogućeno. U provjerenim scenarijima nema novog otkrivanja privatnog članstva.
- Ponovljeni `Test-ProductDemo.cjs`: još **28 HR/EN axe provjera**, glavni demo sadržaj i projektne granice privatnosti. `Test-CollaborationDrafts.cjs` i `Test-ReturnContext.cjs` prolaze na aktualnom paketu u privremenim SQL bazama, uključujući stvarne upise, dva sukoba plana, wiki tokove, ispravak vremena, pretragu kroz osam modula i opoziv prava. Ti upisi ne pripadaju čistom demou.
- Na demou su dodatno provjereni odbijeni prijenosi dokumenta i wiki validacija, čitanje nevaljanih filtara, CSV zaglavlja te naslovi i katalog polja. U wiki validaciji sačuvani su početni prazni retci i izvorna revizija 2. Odbijeni dokumenti nisu pohranjeni; broj datoteka ostao je jedan. Jezik i tema administratora vraćeni su na početne postavke.

Ovaj pregled je nova provjera Db2 demoa uz navedene SQL regresije. Nije nova potvrda Oraclea, ciljnog IIS-a/OpenShifta, NVDA, kapaciteta, backup/restore postupka ili stvarnih bankarskih/poštanskih integracija. Git commit/diff nije provjeren na demou bez povezanog repozitorija. Sistemske oznake razlikovane su od namjerno dvojezičnih korisničkih podataka i native tekstova preglednika/Windowsa.

## Nalazi i prioriteti

| ID | Prioritet | Vrsta | Nalaz / predložena dorada |
|---|---|---|---|
| REV4-01 | **P1** | Potvrđen gubitak nacrta | Odbijeni dokument briše naslov i opis. Sačuvati sirovi tekst, zaštititi vraćeni nacrt i dati jasnu HR/EN uputu za ponovni odabir datoteke. |
| REV4-02 | **P2** | Potvrđena nedosljednost filtara | Nevaljan projekt prikazuje sve dopuštene zadatke uz grešku, a nepostojeći projekt/status prazan rezultat uz prikaz opcije za sve zapise. Uskladiti validaciju, prikaz odabira i rezultat; ne širiti upit pri nevaljanom modelu. |
| REV4-03 | **P3** | Potvrđena nedosljednost jezika izvoza | EN CSV ima hrvatska zaglavlja i naziv `zadaci.csv`. Definirati dosljedan HR/EN izvoz uz očuvan podatkovni i sigurnosni ugovor. |
| REV4-04 | **P3** | Potvrđeni generički/pogrešni naslovi | Polja, registracijski zahtjevi i odbijen pristup imaju `Team Manager · Team Manager`; tuđi profil ima `My profile`. Dodati opisne, kontekstualne HR/EN naslove. |
| REV4-05 | **P3** | Prijedlog administrativne orijentacije | Globalni katalog definicija polja otvoren iz administracije označava osobni profil. Razlikovati upravljanje definicijama od vrijednosti pojedinog korisnika. |

## Reprodukcije i kriteriji prihvata

### REV4-01 — odbijeni prijenos dokumenta

Na `/ProjectDocuments?projectId=1` upisati valjan naslov i stvarni opis, odabrati datoteku od **10 MiB + 1 bajt (10.485.761 B)** i poslati. Preglednički obrazac je valjan, poslužitelj ispravno odbija veličinu lokaliziranom porukom, ali vraća prazan naslov i opis. Provjereno u HR i EN, odgovor 200 s greškom, bez novog dokumenta.

Druga reprodukcija koristi malen `.txt`, naslov od tri razmaka i smislen opis: nakon serverske validacije tekst nestaje, odabir datoteke je prazan, nema `data-unsaved-draft=true`, a zaštita odlaska nije aktivna. Datoteku korisnik ponovno odabire; tekstualni nacrt i uputa za taj korak moraju ostati dostupni.

Uzrok: `Pages/ProjectDocuments.cshtml.cs:38` učitava stranicu i vraća `Invalid(...)` bez nacrta u modelu; `Pages/ProjectDocuments.cshtml:12` renderira prazna tekstualna polja. `UploadedFile.cs` pravilno provodi limit od 10 MiB.

Prihvat: nakon greške naslova/opisa, nedostajuće ili prevelike datoteke ostaju sirovi naslov/opis, uključujući prazne retke i razmake; vraćeni nacrt odmah je zaštićen. HR/EN poruka objašnjava potrebu ponovnog odabira datoteke. Ispravljena ponovna predaja u izoliranoj bazi stvara jedan dokument; prethodna odbijanja ne stvaraju zapise. Ovlasti i preuzimanja ostaju očuvani. Dokazi: `edges.json`, `oversize.json`, `hr/en-document-rejected.png`, `hr/en-oversize-document.png`.

### REV4-02 — nevaljani filtri i prikaz rezultata

| URL | Potvrđeno ponašanje u HR/EN |
|---|---|
| `/Issues?ProjectId=bad` | HTTP 200, lokalizirana binding greška, ali svih osam dostupnih zadataka i odabir „Svi dostupni projekti”. |
| `/Issues?ProjectId=2147483647` | HTTP 200, nula zadataka, bez greške; select prikazuje „Svi dostupni projekti”. |
| `/Issues?Status=not-a-status` | HTTP 200, nula zadataka, bez greške; select prikazuje „Svi statusi”. |
| `/Projects?State=wrong` | Nevaljano stanje tiho postaje sva stanja i prikazuju se sva tri projekta. |

Uzrok: `Pages/Issues.cshtml.cs:54` nastavlja dohvat bez provjere `ModelState` i pripadnosti odabira dostupnim projektima/statusima. `Pages/Projects.cshtml.cs:20` nepoznato stanje zamjenjuje s null. Ne postoji potvrda zaobilaženja ovlasti: prikazani zadaci i dalje pripadaju dopuštenom skupu.

Prihvat: definirati dosljednu lokaliziranu obradu nevaljanih, zastarjelih i nedostupnih filtara. Nevaljani model ne smije izgledati kao važeći širi upit; select mora odgovarati stvarnom kriteriju. Za nedostupan/nepostojeći projekt očuvati generičku obradu bez otkrivanja postojanja privatnog zapisa. Provjeriti spremljene upite, povratne URL-ove, CSV i skupne radnje na izoliranim bazama. Dokazi: `edges.json`, `hr/en-invalid-filter.png`.

### REV4-03 — jezik CSV izvoza

Na EN zatražiti `/Issues?handler=Csv&ProjectId=1`. Zaglavlje ostaje `Id,Naslov,Projekt,Status,Prioritet,Nositelj,Rok`, a `Content-Disposition` navodi `zadaci.csv`, jednako kao na HR. Retci s korisničkim sadržajem i nazivima kataloga nisu predmet automatskog prevođenja.

Uzrok: hardkodirani tekst u `Pages/Issues.cshtml.cs:188` i nazivu izlazne datoteke. Prihvat: dogovoriti HR/EN zaglavlja/naziv ili jasno ponuđen stabilan tehnički format; očuvati cijeli filtrirani skup, redoslijed, ISO datume, UTF-8, escaping i postojeću zaštitu formula. Dokaz: `edges.json`, polje `csv`. U ovoj reviziji nisu ponovno ispitivani svi zlonamjerni CSV sadržaji.

### REV4-04 — smisleni naslovi kartica

U oba jezika `/CustomFields?scope=project&entityId=1`, `/IssueFields?projectId=1`, `/RegistrationRequests` i `/Denied` imaju naslov `Team Manager · Team Manager`. Isti generički naslov imaju katalog polja i korisnička/vremenska polja iz početnog pregleda. Naslov `/Profile?id=3` je `My profile · Team Manager` na EN, iako H1 prikazuje Marka Novaka, a aktivna navigacija ispravno pokazuje korisnike.

Uzrok: nedostajuće stavke i statična oznaka profila u `PageLabels.cs`. Prihvat: naslov opisuje svrhu stranice i razlikuje vlastiti/tuđi profil, po potrebi uz autorizirani naziv entiteta. Naslov greške ne smije sadržavati podatke nedostupnog zapisa. Provjeriti HR/EN i čitanje naslova bez JavaScripta. Dokazi: `overview.json`, `edges.json` (`titles`).

### REV4-05 — katalog definicija nije osobni zapis

Otvoriti `/Roles`, zatim „Prilagodljiva polja” → `/CustomFields`. H1 je `Custom fields`, prikazuje se stvaranje definicija za korisnike/projekte/vrijeme, ali aktivna glavna poveznica je `My profile` (`/Profile`). HR pokazuje isto ponašanje.

To je dodatni scenarij bez `entityId`; raniji REV3-06 provjerio je vrijednosti polja konkretnog entiteta. `ActiveNavigationTagHelper.cs:37` za user scope bez entiteta pada na osobni profil. Predlaže se zasebno određivanje administrativnog konteksta kataloga uz očuvanje ispravnih oznaka vlastitih/tuđih vrijednosti i točnih sekundarnih poveznica. Dokazi: `edges.json` (`catalog`), `hr/en-catalog.png`.

## Preporučeni redoslijed

1. **REV4-01:** očuvanje nacrta dokumenta i oporavak nakon greške prijenosa.
2. **REV4-02:** dosljedni filtri i rezultati, uz negativne provjere spremljenih upita/izvoza/skupnih radnji.
3. **REV4-03/04:** jezik izvoza i smisleni naslovi.
4. **REV4-05:** administrativna orijentacija kataloga polja.

Dokazi, tekstovi stranica, snimke i skripte reprodukcije lokalno su u ignoriranoj mapi `artifacts/product-review4-2026-11-24`, uz dnevnike `artifacts/review4-*.log`. Prethodni dokazi treće revizije nisu prepisani. Privatne pristupne datoteke i snimke baze nisu uključene u Git.
