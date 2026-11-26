# Deveta revizija Team Managera — 26. 11. 2026.

Revizija je započela na commitu **8bcf13d**, na stvarnom [lokalnom IIS-u](https://localhost:7443). Početni pregled 82 HR/EN prikaza nije prijavio axe povrede, prelijevanje dokumenta ili JavaScript greške. Dodatni scenariji istodobnog ispravljanja vremena i nevaljanih administratorskih radnji potvrdili su tri nalaza.

| Stavka | Prioritet | Nalaz i završno ponašanje |
|---|---|---|
| REV9-01 | P2 | Sukob ispravka vremena čuvao je nacrt, ali nije omogućavao siguran oporavak. Sada se prikazuju aktualni datum, sati, aktivnost i napomena; zamjena zahtijeva izričit pregled aktualnog tokena. Drugi sukob ponovno odbija upis uz očuvan tekst i pokušavani token. |
| REV9-02 | P1 | Nevaljani bool parametri u uređivanju rola, dodjeli globalnih rola, novoj vrijednosti šifrarnika i prijelazu tijeka rada pretvarali su se u false i izvršavali zadanu radnju. Sada odgovor 400 prethodi upisu. Valjane radnje i postojeće provjere ovlasti ostaju očuvane. |
| REV9-03 | P2 | Odbijena administratorska radnja preusmjeravala je na lokaliziranu stranicu odbijenog pristupa koja je vraćala 200. Stranica sada vraća 403 i zabranjuje spremanje odgovora u predmemoriju. |

## Reprodukcija

`Test-TimeAdminRecovery.cjs --observe` na izvornom IIS paketu potvrdio je:

- Nacrt ispravka vremena ostaje nakon konkurentne promjene, ali nema kontrole pregleda i ponovnog pokušaja. Postojeća zaštita odlaska bila je aktivna; ovaj nalaz ne tvrdi da je vrijeme gubilo tekst.
- `create=bad` uz naziv vlastite postojeće testne role promijenio je njen opis i ovlasti umjesto odbijanja zahtjeva.
- `remove=bad` dodijelio je testnu globalnu rolu vlastitom testnom računu. Dodjela je potom uklonjena valjanom radnjom.
- `closed=bad` i `remove=bad` na vlastitim testnim stavkama tijeka rada prihvaćeni su kao false. Ovi zahtjevi bili su prijavljeni kao puni administrator; nije potvrđeno zaobilaženje ovlasti drugog korisnika.
- Dodatna provjera računa bez prava uređivanja rola dobila je stranicu odbijenog pristupa s 200, premda je sama promjena bila blokirana. To je zaseban problem HTTP odgovora, ne uspješno neovlašteno uređivanje.

## Implementacija i provjere

Ispravak vremena zadržava izvorni token i sirovi tekst u obrascu. Aktualni spremljeni podaci prikazani su odvojeno. Potvrda pregleda preuzima samo prikazani token, dok spremište i dalje uspoređuje token i ponovno provjerava ovlasti unutar postojećeg postupka upisa. Nakon ponovnog sukoba ponovno se dohvaćaju aktualni unos i prava prije prikaza nacrta. Povratni kontekst izvještaja/zadatka ostaje očuvan. Nova poruka i prikaz imaju HR/EN tekst.

U četiri administratorska handlera vezanje parametara provjerava se prije promjene. Nevaljani `create`, `remove` ili `closed` vraćaju 400, uz očuvane prethodne ovlasti, članstva i tijek rada. Valjani dodavanje/uklanjanje i dalje rade. Stranica Denied ima vlastiti PageModel s 403 i no-store; antiforgery middleware može konačno zaglavlje zapisati kao `no-cache, no-store`.

Samostalni paket `artifacts/review9-final` izgrađen je bez promjene shema baze ili protokola zaključavanja. Novi test **scripts/Test-TimeAdminRecovery.cjs** prošao je na izoliranim SQL Server, Oracle i Db2 bazama te na stvarnom IIS-u. Pokriva HR/EN, ponovljeni sukob, očuvan tekst s početnim praznim retcima i HTML kodiranjem, ispravak validacije, odbijene administrativne radnje bez promjene podataka, valjane dodjele/uklanjanja, axe na stranici sukoba, odbijenu promjenu role i opoziv projektnog pristupa bez prikaza privatnog nacrta. IIS provjera dodatno potvrđuje lokaliziranu HR/EN Denied stranicu, 403 i no-store.

## IIS objava i prihvat

Paket je objavljen pomoću `Update-LocalIisPreview.ps1` nakon Windows UAC potvrde. Zadržani su postojeća testna baza i konfiguracija, verificirani COPY_ONLY backup, hash paketa i HTTPS readiness 200. Novi direktorij: `C:\inetpub\TeamManager-LocalTest\review-20261126-072605`. Manifest: `artifacts/review9-iis-update.json`.

Na novom IIS paketu prolaze `Test-IisLocalAcceptance.cjs` i `Test-IssueDraftRecovery.cjs`: HR/EN glavni tokovi projekata, zadataka, komentara, vremena, pretrage, CSV izvoza, mobilni izvještaj, 404, axe te prethodni popravci nacrta zadatka, komentara i privatne bilješke.

Dokazi u ignoriranim lokalnim artefaktima: `review9-observe.log`, `review9-sql.log`, `review9-oracle-test.log`, `review9-db2-test.log`, `review9-iis-regression.log`, `review9-iis-acceptance.log`, `review9-iis-issue-regression.log`.

NVDA i opterećenje nisu ponovno mjereni. Pregled ne potvrđuje odsutnost svih mogućih grešaka; ciljno produkcijsko okruženje ostaje zasebna provjera.

Ponovljeni pregled novog IIS paketa prošao je **82 HR/EN prikaza**, uz provjeru očekivanih HTTP statusa (uključujući 403 za Denied i 404 za nepostojeću stranicu): nema novih axe prijava, prelijevanja dokumenta ni JavaScript grešaka. Dokazi: `artifacts/review9-overview-final.log` i `artifacts/product-review9-final-2026-11-26/overview.json`.

Postojeće regresije usklađene su s novim 403 odgovorom i porukom sukoba vremena. Na izoliranom Db2 ponovno su prošli `Test-TimeCorrection.cjs` (vlastiti/tuđi autor, upravitelj, povijest, zbroj izvještaja i opoziv članstva) i `Test-ExportTitlesCatalog.cjs` (HR/EN CSV, naslovi, navigacija s/bez JavaScripta, axe i privatni/opozvani pristup). Windows PowerShell 5.1 helper za eksplicitni 403 odgovor provjeren je odvojeno bez poslovnih upisa. Cijeli legacy Test-Users.ps1 i Test-OracleWorkspace.cjs nisu ponovno pokretani; u njima su ažurirana očekivanja 403. Evidencija: `review9-time-correction.log`, `review9-export-titles.log`, `review9-ps-helper.log`.

Sva tri nalaza REV9 zatvorena su u provjerenom lokalnom opsegu. Čisti demo na 7610 i postojeći pregled na 7589 nisu prebacivani na novi paket niti korišteni za testne poslovne upise.
