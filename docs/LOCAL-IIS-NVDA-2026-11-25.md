# Lokalni IIS i NVDA prihvat — 25. 11. 2026.

Aktualna aplikacija provjerena je na **stvarnom lokalnom IIS-u**, [https://localhost:7443](https://localhost:7443), site/pool `TeamManager-LocalTest`, SQL Server baza `TeamManager_IisTest`. IIS nije zamijenjen dotnet preview procesom. Windows paket je `artifacts/iis-local-verified`; konfiguracija prethodnog sitea i postojeći podaci sačuvani su.

## Pronađeno i popravljeno

| Nalaz | Popravak i dokaz |
|---|---|
| **QA-LOCAL-01** — Ponovno izvođenje SQL instalacije pada nakon proširenja kataloga dodatnim stupcima. | Migracija `007-workflow.sql` sada navodi stupce početnih INSERT naredbi. SQL Server prethodno je pri ponovnom izvođenju odbijao kompilaciju starog INSERT-a bez popisa stupaca, iako se grana migracije više ne izvršava. `Test-Package.ps1` ponovno prolazi novu instalaciju, standardni katalog, dodavanje demoa i ponavljanje instalacije bez duplikata. |
| **QA-LOCAL-02** — Prazan opis projekta odbija se kao obvezan, iako ga obrazac prikazuje neobveznim. | Metapodatak opisa zadržava prazni string pri model bindingu. Izričito prazna vrijednost sada prolazi stvarnu pregledničku predaju u HR i EN. Obveznost naziva i ograničenje opisa na 4000 znakova ostaju očuvani. Izvorna greška reproducirana je tijekom ručne NVDA provjere. |
| **QA-LOCAL-03** — IIS prije aplikacije odbija 32 MiB prijenos tehničkom stranicom 413.1, bez lokalizirane upute. | Objavljeni `web.config` čuva aplikacijska tijela grešaka pomoću `existingResponse="PassThrough"`. Tvrdi vanjski IIS limit iznosi 64 MiB, dok je upravljani limit izričito zadržan na 30.000.000 B, a prihvat datoteke na 10 MiB. To omogućuje aplikaciji da poznatu preveliku duljinu odbije prije čitanja tijela uz HR/EN 413. Provjereni no-JS 32 MiB odgovor ima aplikacijski naslov/uputu, a povratak u Edgeu vraća tekst. |

Razlikovanje IIS request filtera i upravljanog limita prati [Microsoftove upute za IIS](https://learn.microsoft.com/en-us/aspnet/core/publishing/iis). Vanjski limit je konačan; ovo nije tvrdnja da aplikacija obrađuje zahtjeve koje neki raniji poslužitelj odbije prije nje. Veće datoteke nisu postale dopuštene za pohranu.

## IIS objava i operativne provjere

`scripts/Update-LocalIisPreview.ps1` ažurira isključivo postojeći namjenski site s loopback HTTPS vezama na 7443 i navedenom lokalnom SQL bazom. Provjerava cilj, stvara **COPY_ONLY/CHECKSUM backup i izvodi RESTORE VERIFYONLY**, kopira novi paket u novi direktorij, čuva lokalnu konfiguraciju i poznate lokalne podatkovne direktorije, zaustavlja pool, pokreće idempotentne migracije, mijenja site pointer i ponovno provjerava readiness te hash objavljenog DLL-a. Prethodni paketi i backup ostaju dostupni. Pri neuspjehu vraća pointer; ne vraća niti briše bazu automatski.

Windows UAC potvrde izvršene su kroz standardni Windows upit; trajna politika izvršavanja PowerShella nije mijenjana. Uspjeh objave potvrđen je statusnom datotekom, HTTPS readiness 200 i pregledničkim testovima. Nova SQL shema ima verziju **43**. Ovaj korak potvrđuje verificirani backup, a ne novu probnu obnovu baze iz njega.

Prolaze:

- `Test-IisLocalAcceptance.cjs`: stvarni `Microsoft-IIS` odgovor, HR/EN pregledničko stvaranje projekta s praznim opisom, novi zadatak, komentar, vrijeme, pretraga i povratni kontekst, CSV, lokalizirani 404 bez IIS tehničke stranice, mobilni izvještaj na 320 px i axe.
- `Test-UploadRegistrationRecovery.cjs --registration-disabled`: granice 0 B, 10 MiB, 10 MiB + 1 B i 32 MiB; klijentska zabrana zadržava tekst, ispravljena predaja uspijeva; bez JS-a stvarni HR/EN 413 i povratak na tekst. Isključena registracija ostaje 404 na oba jezika. Registracijska validacija ne proglašava se novim IIS testom dok je registracija isključena.
- `Test-DocumentDraftsFilters.cjs`: HR/EN nacrti dokumenata, serverska provjera neovisna o JS-u, ponovni odabir datoteke, odlazak/reload, ispravljena predaja, nevaljani/ponovljeni filtri, spremljeni upiti, CSV/skupna radnja i opoziv prava.
- `Test-FieldBulkDrafts.cjs`: vraćeni nacrti polja i izvorne revizije, ponovljeni sukobi, izričit pregled, oba skupna odabira i status/prioritet, zadatak izvan filtra, uspješan oporavak i opoziv članstva dok su nacrt/skupna radnja otvoreni. Test sada vraća i početni jezik administratora u svojem završnom čišćenju.

## Stvarni NVDA — HR i EN

Prethodna prijenosna kopija NVDA 2026.3.1 rušila se pri pokretanju u `ucrtbase.dll`. Preuzeta je čista kopija iz [službenog izdanja NV Accessa](https://github.com/nvaccess/nvda/releases/tag/release-2026.3.1), provjereni su objavljeni SHA-256 i Authenticode potpis te je napravljena izdvojena prijenosna testna kopija/profil. Postojeći profil nije prepisan. Postupak prijenosne kopije i debug zapisivanja opisan je u [NVDA uputama](https://api.nvaccess.org/documentation/userGuide.html).

Ručni dio izveden je preko Windows Computer Use u Edgeu, uz aktivan NVDA i stvarni zapis `Speaking` događaja. Potvrđeni su HR pregled naslova tipkom H, oznake/obveznost polja, slijed tabulatora i kontrole projekta, serverska greška s upozorenjem/fokusom, stvaranje namjenskog projekta i HR/EN zadataka, komentari, sati s povezanom uputom te EN pretraga i otvaranje rezultata. Za dva ručna testna zadatka izravno su potvrđeni po jedan spremljeni unos vremena od 1 h. Profil je vraćen na hrvatski. Hrvatski nazivi NVDA kontrola i Windows dijelova date pickera nisu zamijenjeni za neprevedeno aplikacijsko sučelje; zapis potvrđuje i promjenu jezika čitanja dokumenta na engleski.

Potom `scripts/Test-NvdaIis.cjs` prolazi **24 stvarne govorne provjere, po 12 na HR i EN**, na konačnom IIS paketu: skip-link i fokus glavnog sadržaja, broj radnika, zaštićena lozinka, neuspjela prijava/fokus na `sign-in-errors`, aktivna poveznica projekata, naslov zadatka, gumb spremanja, višeredni komentar, sati i uputa, pojam pretrage, rezultat i odjava. Test koristi vidljivi Edge i programatski fokus/akcije preglednika te uspoređuje **novi izlaz pokrenutog NVDA-a** s očekivanom najavom. Ne generira vlastite govorne poruke i ne zamjenjuje NVDA DOM-om ili axe rezultatom. Stvarni upisi i odjava/protected redirect također su potvrđeni.

To je provjera navedenih glavnih tokova, a ne formalna potvrda potpune WCAG usklađenosti ili svakog modula/čitača zaslona.

## Kratko lokalno opterećenje

`Test-IisLoad.cjs`: šest autentificiranih ruta, po 10 sekundi za svaku razinu, ukupno **2710 odgovora bez HTTP grešaka**.

| Paralelnost | Odgovori | Medijan | p95 | Maksimum |
|---:|---:|---:|---:|---:|
| 1 | 159 | 62 ms | 113 ms | 542 ms |
| 10 | 1432 | 64 ms | 143 ms | 228 ms |
| 25 | 1119 | 221 ms | 301 ms | 526 ms |

Provjereni su i liveness/readiness, odbijanje nepoznatog Host zaglavlja, nedostupnost lokalne konfiguracije, anonimno preusmjeravanje na prijavu, Secure/HttpOnly/SameSite=Lax cookie te odbijanje POST-a bez antiforgery tokena. Rezultati vrijede za ovaj laptop, sadašnje testne podatke i navedeni kratki uzorak; nisu kapacitet ciljne produkcije niti izravna usporedba sa starim mjerenjem.

## Dokazi i završno stanje

Lokalno i izvan Gita: `artifacts/iis-nvda-20261125` s manifestima objave, logovima paketa/testova, mjerenjem opterećenja te `manual-speech.json` i `automated-speech.json`. Sirovi NVDA zapis može sadržavati okolne sistemske najave i ne objavljuje se. Pristupne konfiguracije, testni profil i certifikacijski privatni ključevi nisu dio commita.

Glavni URL za ovaj prihvat je [https://localhost:7443](https://localhost:7443). Dotnet demo i postojeći Db2 pregled zadržavaju [7610](https://localhost:7610) i [7589](https://localhost:7589); njihov paket je `artifacts/iis-a11y-complete`. Njihovi poslovni podaci nisu korišteni za ove IIS upise.

Lokalni IIS/NVDA dio navedenog opsega je zatvoren. Ciljni bankarski IIS/OpenShift i kapacitet stvarnog okruženja ostaju zasebni uvjeti, bez nove tvrdnje o produkcijskoj spremnosti.
