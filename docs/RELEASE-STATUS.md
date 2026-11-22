# Status izdanja Team Manager

Provjereno lokalno 19. 11. 2026. SQL Server, Oracle i Db2 LUW podržavaju aktivne module: račune i ovlasti, projekte i zadatke, planove, vrijeme, prilagodljiva polja, dokumente, wiki, novosti, forum, Git poveznice/povijest, ulaznu e-poštu, obavijesti, SMTP/podsjetnike, osobne API tokene, registraciju i grupe. HR/EN sučelje je zadržano. Početni legacy pregled timskih stavki u Oracle načinu otvara aktivni pregled projekata.

## Lokalna provjera

**REV-10, 22. 11. 2026.:** proširena pretraga radi u HR/EN na sva tri providera. `Test-ContentSearch.cjs` provjerava sadržaj wikija, metapodatke datoteka, tijelo novosti i foruma, nastavak nakon 20 rezultata te trenutno uklanjanje rezultata nakon opoziva članstva. Pretraga datoteka obuhvaća naziv/opis/ime datoteke, ne tekst binarnog priloga. Povijest wikija pretražuje se kroz trenutačnu verziju.

**REV-03/06/07/11, 22. 11. 2026.:** test s 52 zadatka potvrđuje 50+2 rezultata, izvoz svih 52 u CSV, povratak sa zadatka na iste filtre/stranicu i odbijanje vanjskog povratnog URL-a. Projekti imaju filtar, pregled i odvojene postavke; kartice imaju lokalizirane naslove i navigacija označava trenutačni odjeljak. SQL Server i Db2 provjere prošle su na paketu `rev-navigation-final`, Oracle i na završnom paketu s ciljanim dohvatom odredišta prijenosa (`rev-content-final`). Svaka izmjena zadatka i dalje ponovno provjerava prava unutar transakcije.

**REV-02/04/05/08, 22. 11. 2026.:** uvedena je zaštita nespremljenih POST obrazaca, fokus i uloga upozorenja na serverskim greškama, dopunjeni pronađeni EN prijevodi i usklađena ograničenja šifrarnika. `Test-FormRevision.cjs` prolazi na SQL Serveru, Oracleu i Db2, uključujući HR/EN i axe provjeru stanja greške. To nije nova ručna NVDA potvrda. Regionalni format može se zadati neovisno od jezika kroz `Formatting:Culture` (zadano hr-HR/en-GB).

**REV-01 dovršen 22. 11. 2026.:** dohvat projektnih članstava sada uključuje promatrača i primjenjuje serverske ovlasti. `Test-ProfilePrivacy.cjs` prošao je na privremenom SQL Serveru te izoliranim Oracle i Db2 shemama u HR/EN, uključujući javni, vlastiti, administratorski, naslijeđeni i opozvani pristup. Ostale revizijske dorade ostaju otvorene.

**Revizija proizvoda 20. 11. 2026.:** pronađeno je 14 otvorenih dorada, uključujući P1 otkrivanje naziva privatnih projekata i članskih uloga kroz tuđi profil. Na Db2-u je potvrđeno da ograničeni korisnik te metapodatke vidi kroz profil, dok izravan pristup provjerenom privatnom zadatku ostaje HTTP 404. Prethodni prolaz regresija ne pokriva taj nalaz. Prije korisničkog pilota zatvoriti P1 stavke iz [revizije proizvoda](PRODUCT-REVIEW-2026-11-20.md).

- Aktivni paket: `artifacts/oracle-complete-final`, Oracle shema 10 / protokol zaključavanja 5.
- URL: https://localhost:7572/Projects; readiness 200.
- Oracle integracije potvrđuju migracijski integritet 51 tablice, očuvanje podataka/povijesti, ovlasti, zaključavanja, paralelne upise, rollback, opozive i zaštitu zadnjeg administratora.
- Stvarni loopback TLS IMAP i SMTP testovi potvrđuju stvaranje privatnih zadataka, deduplikaciju, commit/Seen, HR/EN poruke, ponovne pokušaje, lease zaštitu i podsjetnike.
- Cjeloviti Oracle browser tokovi i 52 automatizirana WCAG A/AA skeniranja su prošli; odvojeni API, registracijski, grupni i e-mail testovi provjeravaju dodatne autentikacijske i UI tokove.
- SQL integracijske regresije i zasebni privremeni SQL testovi registracije/grupa su prošli.
- Db2 Windows paket: `artifacts/db2-complete-final`, [pregled](https://localhost:7589/Projects), readiness 200. Linux slika `team-manager:db2-qa` radi kao UID 1654 iza [HTTPS proxyja](https://localhost:7591/Projects); sesija je potvrđena nakon restarta. Upute: [DB2-LOCAL.md](DB2-LOCAL.md).
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
