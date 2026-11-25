# Šesta revizija Team Managera — 25. 11. 2026.

Pregledan je paket `artifacts/rev5-complete`, aplikacijski kod commita **e783051**, na [lokalnom Db2 demou](https://localhost:7610). Nakon zatvaranja svih pet nalaza REV5 ponovljen je pregled glavnih HR/EN tokova. **U provjerenom lokalnom opsegu nema novih potvrđenih nedostataka ni novih REV6 stavki.**

## Pregled i rezultati

| Provjera | Rezultat |
|---|---|
| 82 prikaza, po 41 HR/EN, desktop 1440 px i mobilnih 320 px | Bez prelijevanja cijelog dokumenta, bez JS `pageerror` događaja. |
| 82 axe provjere WCAG 2 A/AA i 2.1 A/AA | Bez prijavljenih povreda u tim prikazima. |
| Završni `Test-ProductDemo.cjs`, dodatnih 28 HR/EN axe provjera | Prolazi; čisti demo zadržava tri projekta, osam zadataka i povezani sadržaj. |
| Voditelj, razvojni korisnik i promatrač | Dopušteni pristup radi; promatraču privatni projekt/zadatak/izvještaj daju 404, tuđi profil ne otkriva privatno članstvo, uređivanje je onemogućeno. Administrativne stranice odbijaju nedopušten pristup. |
| Naslovi u 82 prikaza | Nema generičkog `Team Manager · Team Manager`; početna promjena lozinke sada ima opisni HR/EN naslov. |
| Nacrti i skupne radnje na SQL Serveru, Oracleu i Db2 | Prolaze HR/EN regresije, uključujući ponovno nastali sukob i opoziv članstva dok su nacrti otvoreni. |
| Prethodni dokumenti i filtri na konačnom SQL paketu | Prolazi `Test-DocumentDraftsFilters.cjs`, uključujući neovisno serversko odbijanje nakon namjernog zaobilaženja nove JS provjere veličine. |
| Prijenos i registracija na konačnom SQL paketu | Prolazi `Test-UploadRegistrationRecovery.cjs`: granice veličine, oporavak, no-JS 413, lokalizacija, kodiranje, fokus, axe i opisni naslovi. |

82 prikaza obuhvaćaju projekte, zadatke i novi zadatak, grupiranje, pretragu, kalendar/Gantt, aktivnosti, planove, wiki, dokumente, novosti, forum, repozitorije, vrijeme i njegove detalje, obavijesti, audit, workflow, korisnike/profil, ovlasti, grupe, zahtjeve za registraciju, prilagodljiva polja/definicije, osobne postavke/tokene, ulaznu poštu i stranice grešaka. Pregled je proveden u svijetloj temi. Vizualno je dodatno provjeren prikaz vraćene skupne radnje s oba odabira, uključujući zadatak koji više nije u filtru.

## Zatvaranje prethodnih nalaza

- **REV5-01:** sirovi nacrt polja i pokušavana revizija ostaju nakon sukoba; aktualna vrijednost prikazana je odvojeno, zaštita odlaska aktivna. Nove revizije zahtijevaju izričit pregled. Zajednička polja prolaze i drugi sukob prije uspješnog oporavka; polja zadatka prolaze sukob i oporavak. Opoziv članstva odbija pokušaj i ne prikazuje privatni nacrt.
- **REV5-02:** oba odabira, status i prioritet ostaju nakon sukoba, čak i kada jedan zadatak više nije u filtru. Drugi sukob ponovno odbija upis; pregled i ponovna potvrda omogućuju uspješnu pohranu. Opoziv članstva daje 404 prije ponovne skupne predaje.
- **REV5-03:** JS ne šalje praznu/preveliku datoteku i čuva tekst. Potvrđene su granice 0 B, 10 MiB, 10 MiB + 1 B i 32 MiB te uspješan prijenos manje datoteke nakon korekcije. Poznata duljina zahtjeva iznad HTTP limita bez JS-a daje HR/EN HTML 413; u provjerenom Edgeu povratak vraća naslov i opis. Serverski limit nije povećan, odbijeni prijenos ne stvara dokument.
- **REV5-04:** pogrešno potvrđena lozinka vraća broj radnika i ime, uključujući razmake i sigurno kodirane HTML znakove. Lozinke ostaju prazne; fokus i prevedena greška rade.
- **REV5-05:** registracija i početna promjena lozinke dobivaju serverom generirane opisne HR/EN naslove.

Prethodno zatvoreni kriteriji prve četiri revizije ostaju povijesni dokaz svojeg opsega. Ovaj pregled nije ponovno izvršavanje svakog starog integracijskog testa; svježe regresije navedene su iznad.

## Preostali uvjeti izvan lokalnog pregleda

Ciljni IIS/OpenShift, proxy koji odbija prijenos prije aplikacije, NVDA i bankarske integracije nisu ponovno potvrđeni u ovom krugu. Automatski axe rezultat nije potvrda potpune pristupačnosti. Git commit/diff nije pregledan na čistom demou bez povezanog repozitorija. Tamna tema i ostali preglednici nisu dio ovog novog pregleda. Ti uvjeti ostaju u osnovnom backlogu; nisu novi potvrđeni nedostaci aplikacije iz ovog kruga.

Novi prioriteti dorada nisu dodani jer nema novih reproduciranih nalaza u navedenom opsegu. Prije ciljne produkcijske objave provesti preostale provjere u stvarnom okruženju.

## Dokazi i stanje

Lokalno, ignorirano: `artifacts/product-review6-2026-11-25/overview.json`, HR/EN tekstualne snimke i mobilne slike; `artifacts/rev6-overview.log`, `rev6-demo-check.log`; zasebni SQL/Oracle/Db2 `rev5-final-*.log` i `artifacts/rev5-draft-regression/<port>/isolated.json`. Verzije ponovljivih regresija pohranjene su u repozitoriju.

Oba pregleda ostaju dostupna: [demo](https://localhost:7610) i [postojeći radni podaci](https://localhost:7589), readiness 200. Poslovni demo podaci sačuvani su, administratorov jezik/tema vraćeni na početne vrijednosti. Privremene SQL baze i vlastiti izolirani testni web procesi uklonjeni su nakon provjera.
