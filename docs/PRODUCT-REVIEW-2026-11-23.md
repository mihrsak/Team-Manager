# Druga revizija Team Managera — 23. 11. 2026.

Pregledani commit: `1f174a0`, paket `artifacts/review-release`, demo [https://localhost:7610](https://localhost:7610), Db2 LUW, relacijska shema 11. Nakon restarta računala pokrenuti su postojeći Podman VM, postojeći Db2 kontejner i postojeća demo konfiguracija. Baza nije ponovno stvarana.

Pronađeno je **10 preostalih nalaza: dva P1, pet P2 i tri P3**. Prvo treba zaštititi nacrt pri sukobu wiki izmjena i održati zaštitu nespremljenih izmjena nakon preuzimanja datoteke. Novi nalazi imaju oznake **REV2-01–REV2-10**; uključuju dodatne scenarije u područjima ranijih dorada, čije prethodne provjere nisu pokrivale ove slučajeve.

Naknadni status 23. 11. 2026.: **REV2-01 i REV2-02 su popravljeni i provjereni na SQL Serveru, Oracleu i Db2, HR/EN**. Izvorni nalazi ispod ostaju dokaz revizije pregledanog commita. Aktualni paket je artifacts/rev2-drafts-final; detalji provjera i otvorenih stavki su u [RELEASE-STATUS.md](RELEASE-STATUS.md) i [BACKLOG.md](BACKLOG.md).

## Što je provjereno

- **44 prikaza**: 22 glavne stranice na HR i EN, desktop 1440 px i mobilni pregled 390 px. Dashboard, projekti, zadaci/novi zadatak, kalendar/Gantt, aktivnosti, pretraga, plan, wiki, dokumenti, novosti, forum, izvještaj i detalji vremena, obavijesti, administrativna revizija, tijek rada, profil i korisnici. Svi navedeni autorizirani prikazi vratili su HTTP 200.
- **24 axe provjere**: projekt, zadatak, wiki, izvještaj vremena, detalj vremena i tijek rada; HR/EN, svijetla/tamna tema. Nema prijavljenih WCAG 2 A/AA i 2.1 A/AA povreda u tim automatiziranim provjerama. Zasebno je izmjeren reflow na 320 px; automatizirani prolaz nije otkrio potvrđeni problem širine.
- Demo voditelj, razvojni korisnik i promatrač: privatni projekt/zadatak dostupni su prvim dvjema ulogama, promatraču vraćaju 404; privatno članstvo nije u njegovu prikazu tuđeg profila. Administrativne stranice preusmjeravaju ograničene uloge na odbijen pristup. Promatračev naslov zadatka doista je onemogućen (`disabled`), bez gumba spremanja.
- Upozorenje pri običnom napuštanju izmijenjenog zadatka radi na HR/EN: odustajanje zadržava naslov. Lokalizirani wiki konflikt prikazuje fokusirani sažetak greške, ali ne zadržava nacrt.
- Negativni POST scenariji: zastarjela wiki revizija, nevaljan datum novog vremena i obavezna aktivnost ispravka vremena. Preuzeta je postojeća demo datoteka. Nisu stvarani novi projekti, zadaci, komentari ni dokumenti. Wiki je ostao na reviziji 2, a izvještaj na tri postojeća unosa vremena. Jezik i tema administratora vraćeni su na početne postavke.

Dokazi su lokalno u ignoriranoj mapi `artifacts/product-review-2026-11-23`: snimke, tekstovi stranica, `pages.json`, `edges.json`, `download.json`, `additional.json` i skripte reprodukcije. Pristupne tajne nisu uključene u izvještaj niti Git.

Ovo nije nova potvrda SQL Servera/Oraclea, IIS-a/OpenShifta, ručnog NVDA testa, opterećenja, bankarskih integracija ni SMTP/IMAP isporuke. Pozitivni unosi i brisanja podataka nisu ponavljani na čistom demou. Nalazi zajedničkog PageModel/JS koda mogu zahvatiti više providera, ali ovdje su reproducirani samo na Db2 demou.

## Nalazi i prioriteti

P1 znači visok prioritet prije šireg pilota zbog mogućeg gubitka korisničkog rada; P2 sljedeću funkcionalnu iteraciju; P3 robusnost i poboljšanja uporabljivosti. U provjerenim scenarijima nije pronađeno novo otkrivanje privatnog projekta ili članskih uloga.

| ID | Prioritet | Vrsta | Nalaz i predloženi rezultat |
|---|---|---|---|
| REV2-01 | **P1** | Potvrđena greška | Wiki pri zastarjeloj reviziji zamjenjuje korisnikov nacrt aktualnim sadržajem; odbijeni novi unos vremena prazni aktivnost/napomenu i vraća današnji datum. Zadržati unesena polja nakon greške te kod konflikta ponuditi nacrt i aktualnu verziju zasebno. |
| REV2-02 | **P1** | Potvrđena greška | Nakon preuzimanja datoteke `submitting` ostaje uključen. Čak ni nove izmjene nakon dovršenog preuzimanja više ne aktiviraju `beforeunload` zaštitu. Zaštita treba ostati aktivna nakon radnji koje ne napuštaju dokument. |
| REV2-03 | **P2** | Potvrđena greška prikaza | Tablica vremena na detalju zadatka širi cijelu stranicu: pri 390 px dobiveno je 562 px na HR i 583 px na EN; isto se događa na 320 px i u obje teme. Omogućiti vodoravno pomicanje samo tablice ili prilagodljiv prikaz unosa. |
| REV2-04 | **P2** | Potvrđena greška lokalizacije | HR serverska validacija detalja vremena prikazuje `The Activity field is required.` Lokalizirati obavezna polja i nazive polja, uz očuvanje nacrta i pristupačnog sažetka. |
| REV2-05 | **P2** | Potvrđen UX nedostatak | Otvaranje rezultata pretrage gubi pojam/modul pri povratku; detalj vremena nema povratak na filtrirani izvještaj. Prenijeti validirani lokalni povratni kontekst kroz povezane module i njihove obrasce. |
| REV2-06 | **P2** | Potvrđena greška regionalnog formata | EN Gantt prikazuje `23.11.2026 – 08.12.2026`, dok EN zadatak koristi `23/11/2026` i `08/12/2026`. Format datuma konstruira se u PageModelu, izvan prethodno usklađenih Razor prikaza. Primijeniti odabranu kulturu na opis raspona. |
| REV2-07 | **P2** | Potvrđen UX nedostatak | Detalj vremena ne prikazuje autora, naziv/broj zadatka ni projekt prije obrasca ispravka. Dodati taj kontekst i objašnjenje prava uređivanja kako bi korisnik mogao potvrditi da uređuje pravi unos. |
| REV2-08 | **P3** | Prijedlog uporabljivosti | Povijest zadatka prikazuje tehnički JSON s `Before`, `After`, `AssigneeId`, `Revision` i drugim poljima. Dodati čitljiv popis promijenjenih polja sa starim/novim vrijednostima i imenima umjesto ID-ova; izvorni zapis može ostati u dodatnim detaljima. |
| REV2-09 | **P3** | Prijedlog uporabljivosti | Nedostupan projekt/zadatak vraća prazan 404 odgovor. Prikazati generičku HR/EN stranicu s povratkom u aplikaciju, uz očuvanje 404 i bez otkrivanja postoji li privatni zapis. |
| REV2-10 | **P3** | Potvrđen rubni slučaj | Prazna druga stranica pretrage nema poveznicu za prethodnu/prvu stranicu, jer se navigacija prikazuje samo unutar neprazne grupe rezultata. Odvojiti navigaciju od postojanja rezultata ili normalizirati stranicu. |

## Reprodukcija P1 nalaza

### REV2-01 — nacrt nakon serverske greške

Wiki, HR i EN:

1. Otvoriti `/Wiki?projectId=1&wikiId=1`, čija je aktualna revizija 2.
2. Simulirati zastarjeli otvoreni obrazac: poslati postojeći naslov, novi nacrt sadržaja i `revision=1` na handler `Save`, s valjanim antiforgery tokenom.
3. Poslužitelj ispravno odbija zastarjelo spremanje i vraća lokaliziranu poruku konflikta.
4. Vraćeni `textarea` sadrži aktualnu spremljenu verziju, a korisnikov novi tekst više nije prisutan. Spremljeni sadržaj i revizija ostaju nepromijenjeni.

Uzrok je eksplicitno postavljanje `DraftBody` i `DraftTitle` na `null` u `src/TeamManager.Web/Pages/Wiki.cshtml.cs:63`, nakon čega prikaz koristi aktualnu reviziju. To je odvojeno od potvrde pri napuštanju stranice: greška nastaje tijekom spremanja vlastitog obrasca.

Drugi reproducirani oblik: na `/Issue?id=2` u unos vremena upisati datum `1752-11-23`, sat `1`, aktivnost i napomenu pa poslati obrazac. Datum nije prihvatljiv poslužitelju; poruka greške je lokalizirana, ali aktivnost i napomena su prazne, a datum je vraćen na današnji. `OnPostTimeAsync` čisti ModelState i ponovno učitava stranicu (`Issue.cshtml.cs:90`), dok obična polja tog obrasca ne prikazuju vraćene unesene vrijednosti.

Kriterij prihvata: oba jezika zadržavaju sva unesena polja pri odbijenom spremanju; wiki konflikt ne prepisuje nacrt, ne radi automatski overwrite nove revizije i omogućuje kopiranje/usporedbu nacrta s aktualnim sadržajem. Nevaljano vrijeme ne stvara novi unos niti događaj.

### REV2-02 — zaštita nakon preuzimanja

1. Otvoriti `/ProjectDocuments?projectId=1` i upisati naslov novog dokumenta bez slanja obrasca.
2. Provjeriti da `beforeunload` zahtijeva ostanak/potvrdu: događaj je otkazan zbog nespremljenih izmjena.
3. Kliknuti postojeću datoteku `review-plan.txt` i prihvatiti potvrdu odlaska. Preglednik preuzima datoteku, a ista stranica ostaje otvorena.
4. Nakon dovršenog preuzimanja promijeniti naslov u **novi** nacrt. `beforeunload` se više ne otkazuje; zaštita je ugašena i za te nove izmjene.

Rezultat je jednak na HR/EN: prije preuzimanja `beforePrevented=true`, nakon novih izmjena `afterPrevented=false`. Mjeren je otkazivi sintetički događaj u stvarnom Edge dokumentu; preuzimanje i potvrda bili su stvarni. Izvor potvrđuje uzrok: `wwwroot/js/forms.js:21` postavlja trajni `submitting=true` za poveznicu, a `beforeunload` ga provjerava na retku 13. Poveznica za preuzimanje iz projektnih dokumenata nema atribut `download`.

Kriterij prihvata: nakon preuzimanja nove izmjene ponovno aktiviraju zaštitu zatvaranja/ponovnog učitavanja; običan valjan submit može napustiti stranicu; odustajanje i dalje čuva polja; potvrde se ne udvostručuju. Pokriti i ostale radnje koje vraćaju preuzimanje ili ne uzrokuju navigaciju.

## Ostali konkretni koraci i trag u kodu

| ID | Koraci / dokaz | Kriterij prihvata |
|---|---|---|
| REV2-03 | Otvoriti `/Issue?id=2` na 390 ili 320 px; usporediti `document.documentElement.scrollWidth` s `innerWidth`. Odjeljak vremena u `Pages/Issue.cshtml:165` ima tablicu bez izoliranog overflow spremnika. Snimke `hr-issue-mobile.png` i `en-issue-mobile.png`. | Širina dokumenta ostaje unutar viewporta, a svi stupci vremena ostaju dohvatljivi; provjeriti HR/EN i obje teme. |
| REV2-04 | U HR poslati `/TimeEntry?Id=1&IssueId=1` s praznom aktivnosti i ostalim valjanim poljima/tokenom. Rezultat je engleska Required poruka. Polje koristi `[Required]` u `Pages/TimeEntry.cshtml.cs:13`. Ovaj slučaj namjerno provjerava serversku validaciju, mimo HTML `required` provjere. | HR poruka i naziv polja na hrvatskom; EN na engleskom; isti princip primijeniti na ostale DataAnnotations i model-binding poruke. |
| REV2-05 | `/Search?Query=pristup&Module=issues` → zadatak #2 → poveznica povratka vodi na `/Issues?ProjectId=1`. `/TimeReport?ProjectId=1&Person=admin&Activity=Razvoj` → detalj unosa ima samo poveznicu na zadatak. `Search.cshtml.cs:60` ne prenosi povratni URL; `TimeEntry.cshtml` ga nema. | Povratak zadržava pojam/modul/stranicu ili filtre izvještaja; vanjski povratni URL se odbija. Dopuniti i aktivnu glavnu navigaciju za `/TimeEntry` i `/Gantt`. |
| REV2-06 | Prebaciti na EN i otvoriti `/Gantt?ProjectId=1&Month=2026-11`. Opisi raspona koriste točke. `Pages/Gantt.cshtml.cs:44`–46 izričito formatira `dd.MM.yyyy`. | Gantt, zadatak i izvještaji koriste istu konfiguriranu kulturu, neovisno od jezika; HTML date vrijednosti ostaju ISO. |
| REV2-07 | `/TimeEntry?Id=1&IssueId=1`: vidljivi su datum, sati, aktivnost, napomena i opća uputa, bez autora/projekta/naslova zadatka. `Pages/TimeEntry.cshtml` potvrđuje taj opseg. | Prije spremanja ispravka jasno su vidljivi autor i pripadajući zadatak/projekt; ograničena uloga dobiva informativan prikaz bez mogućnosti tuđeg ispravka. |
| REV2-08 | Na zadatku #2 otvoriti „Promjena zadatka” u povijesti. JSON se prikazuje izravno u `pre` (`Pages/Issue.cshtml:166`). | Čitljiv, lokaliziran prikaz promjene; korisnički tekst ne prevoditi automatski. Sačuvati izvorni revizijski trag. |
| REV2-09 | Kao `demo.viewer` otvoriti privatni `/Project?id=2`: HTTP 404, tijelo odgovora duljine 0. Prava rade ispravno; nedostaje produktni prikaz greške. | Generička HR/EN poruka i poveznica na dostupne projekte; isti odgovor za nepostojeći i nedopušten zapis. |
| REV2-10 | `/Search?Query=portal&Module=projects&PageNumber=2` daje prazan rezultat bez „Prethodna stranica”, iako prva stranica ima projekt. Moguće je doći preko ranije spremljenog URL-a ili promjene podataka. `Search.cshtml:21` i 41 vežu navigaciju uz nepraznu grupu. | Prazna kasnija stranica omogućuje povratak ili se usmjerava na valjanu stranicu, uz očuvanje pojma/modula. |

## Preporučeni redoslijed dorada

1. **Zaštita korisničkog rada:** REV2-01 i REV2-02; zajedničke provjere nacrta, odbijenog spremanja, konflikta, preuzimanja i ponovnog učitavanja u HR/EN.
2. **Prikaz i jezik:** REV2-03, REV2-04 i REV2-06; provjere na 320/390 px i lokalizacija serverskih grešaka/regionalnih opisa.
3. **Povezani radni tokovi:** REV2-05 i REV2-07; siguran povratak na izvorni kontekst i prepoznatljiv unos vremena.
4. **Čitljivost i rubni slučajevi:** REV2-08–REV2-10.

Prikazi glavnih modula, odvajanje demo podataka i provjerene granice projektnih ovlasti vidljivo su poboljšani. Prije šireg pilota preporuka je zatvoriti oba P1 nalaza, a zatim provesti korisničku reviziju s nekoliko stvarnih zadataka i ovlaštenih uloga. Vanjski produkcijski uvjeti prihvata iz prethodnog backloga ostaju zasebni.
