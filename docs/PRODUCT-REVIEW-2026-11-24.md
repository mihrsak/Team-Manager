# Treća revizija Team Managera — 24. 11. 2026.

Pregledani commit: `c68fa47`, paket `artifacts/rev2-polish-final`, [lokalni demo](https://localhost:7610), Db2 LUW, relacijska shema 11 / protokol 5. Aplikacija je već bila dostupna; baza nije ponovno stvarana. Pregled je izvršen u Edgeu putem Playwrighta.

Pronađeno je **šest nalaza: jedan P1, tri P2 i dva P3**. Preporučeni prvi zahvat je zaštita nacrta u forumu, novostima i planovima. REV2-01–REV2-10 ostaju povijesno zatvorene stavke za ranije provjereni opseg; ova revizija proširuje scenarije i otkriva i regresiju u obradi HTTP grešaka. Ovdje su dokumentirani nalazi i prioriteti; popravci nisu implementirani.

Naknadni status 24. 11. 2026.: **REV3-01–REV3-04 popravljeni su i provjereni na SQL Serveru, Oracleu i Db2, HR/EN**. Aktualni paket je `artifacts/rev3-mobile-context`; otvorene ostaju REV3-05/06. Izvorni nalazi i reprodukcije ispod ostaju dokaz pregledanog commita. Detalji implementacije i provjera: [RELEASE-STATUS.md](RELEASE-STATUS.md).

## Opseg i pozitivni rezultati

- **60 HR/EN prikaza:** po 30 stranica na svakom jeziku, desktop 1440 px i mobilnih 320 px. Dashboard, projekti i postavke projekta, zadaci i novi zadatak, pretraga i prazna kasnija pretraga, kalendar/Gantt, aktivnosti, plan, wiki, dokumenti, novosti, forum, Git postavke, izvještaj/detalji vremena, obavijesti, audit, tijek rada, korisnici/profil, prilagodljiva polja, API tokeni, e-pošta i 404.
- Autorizirane osnovne stranice vratile su 200, namjerno nepostojeća stranica 404. U tih **60 axe provjera** WCAG 2 A/AA i 2.1 A/AA nije bilo prijavljenih povreda ni JavaScript pageerror događaja. To nije formalna potvrda pristupačnosti: vizualni pregled otkriva nečitljive mobilne tablice i bez prelijevanja cijelog dokumenta.
- Demo voditelj i razvojni korisnik vide privatni projekt #2 i zadatak #5. Promatrač dobiva 404 za privatni projekt, zadatak i projektni izvještaj; u tuđem profilu ne vidi privatno članstvo. Naslov zadatka promatraču je onemogućen, bez gumba spremanja. Ograničene uloge na administrativnim stranicama završavaju na `/Denied`; konačni HTML odgovor te stranice je 200, što samo po sebi nije prolaz ovlasti.
- Potvrđeni su ranije dodani čitljiva povijest, kontekst autora/projekta/zadatka na vremenu, povratak s prazne kasnije pretrage i generički prikaz nedostupnog zapisa. U posljednjoj stavci pronađen je dodatni problem za statuse različite od 404, opisan niže.
- Odbijeni POST scenariji u čistom demou koriste naslov/naziv sastavljen od razmaka: preglednički `checkValidity()` prolazi, poslužiteljski trim ga odbija prije poslovnog upisa. Nisu stvarani projekti, zadaci, teme, novosti ni stavke plana. Jezik i tema administratora vraćeni su na početne postavke.
- Završni `Test-ProductDemo.cjs` ponovno provjerava tri projekta, osam zadataka, suradnički sadržaj, tri unosa vremena, granice privatnosti i **28 dodatnih HR/EN axe provjera**. Dokazi su u ignoriranoj mapi `artifacts/product-review-2026-11-24` te pripadajućim lokalnim dnevnicima.

U zasebnoj postojećoj izoliranoj Db2 testnoj shemi, na istom paketu i privremenom portu 7623, ponovljeni su `Test-ReturnContext.cjs` i `Test-DraftProtection.cjs`. Prolaze HR/EN stvaranje povezanih zapisa, wiki GET/POST, validan ispravak vremena, filtri i povratak, opoziv pristupa, konfliktne wiki izmjene i izričita potvrda aktualne verzije te stvarna preuzimanja dokumenata/privitaka/CSV-a. Ti pozitivni upisi i testni podaci pripadaju izoliranoj shemi, ne čistom demou. Prolaz postojećih testova ne zatvara dodatne grane iz ove revizije.

Ovo je pregled lokalnog Db2 paketa. SQL Server/Oracle, ciljni IIS/OpenShift, ručni NVDA test, opterećenje, bankarski SSO/AD i stvarna SMTP/IMAP isporuka nisu ponovno potvrđivani. Git pregled commitova nije provjeren jer demo nema povezani repozitorij. Nije ispitivan stvarni istodobni sukob uređivača na čistom demou.

## Nalazi i prioriteti

| ID | Prioritet | Vrsta | Nalaz i predložena dorada |
|---|---|---|---|
| REV3-01 | **P1** | Potvrđen gubitak nacrta | Odbijeno stvaranje teme/novosti briše tekst; odbijena izmjena plana vraća spremljeni naziv/opis umjesto nacrta. Sačuvati sva sirova polja i izvorni token revizije te odmah aktivirati zaštitu vraćenog nacrta. |
| REV3-02 | **P2** | Potvrđena uporabljivost | Mobilni popis zadataka i izvještaj vremena stišću stupce na 24–36 px i prelamaju datume/naslove u pojedinačna slova. Proširiti čitljive širine i dostupno lokalno pomicanje ili prilagođeni prikaz redaka na ostale tablice. |
| REV3-03 | **P2** | Potvrđena regresija | Obrada HTTP statusa preusmjerava i 400 odgovor na `/NotFound`, koji status prisilno mijenja u 404. Ograničiti 404 obradu na 404 i očuvati ostale statuse. |
| REV3-04 | **P2** | Potvrđen gubitak konteksta | Kalendar/Gantt/aktivnosti vode na zadatak bez izvornog konteksta; njegov povratak vodi u popis zadataka. Vrijeme otvoreno iz zadatka vraća u opći izvještaj. Proširiti siguran povratni kontekst na te ulazne tokove. |
| REV3-05 | **P3** | Potvrđena EN lokalizacija | Grupiranje zadataka po nositelju na EN prikazuje `Nije dodijeljen · 4 issues`. Lokalizirati sistemski naziv nedodijeljene grupe, bez prevođenja korisničkih imena ili kataloga. |
| REV3-06 | **P3** | Prijedlog orijentacije | Polja projekta, API tokeni i obavijesti e-poštom nemaju aktivan nadređeni odjeljak; Workflow nema aktivnu oznaku u administrativnoj navigaciji. Uskladiti označavanje glavne i sekundarne navigacije. |

P1 je prioritet prije šireg korisničkog rada zbog gubitka napisanog teksta. P2 su sljedeća iteracija funkcionalne pouzdanosti i mobilnog rada; P3 su manji propusti i prijedlozi. U navedenim provjerama nije pronađeno novo otkrivanje privatnog članstva.

## Reprodukcije, uzroci i kriteriji prihvata

### REV3-01 — nacrti izvan wikija i vremena

1. Otvoriti `/Forum?projectId=1`, u novi naslov upisati tri razmaka, a u tekst stvarni nacrt. Preglednik dopušta slanje; poslužitelj javlja lokaliziranu grešku, a `textarea[name=body]` vraća prazan tekst. Isto vrijedi za `/ProjectNews?projectId=1`.
2. Na `/Roadmap?projectId=1` postojećoj stavci promijeniti opis i staviti tri razmaka u naziv. Nakon greške obrazac vraća spremljeni opis i naziv, a novi opis nestaje. U provjerenoj stavci revizija je ostala 0; nije simuliran stvarni konkurentni upis.
3. U oba jezika ispis greške je preveden, ali forum/novosti nemaju vraćeni nacrt ni `data-unsaved-draft=true`.

Uzrok: handleri pozivaju `OnGetAsync`, lokalne POST argumente ne čuvaju u modelu prikaza, a predlošci vraćaju prazna ili spremljena polja. Lokacije: `Pages/Forum.cshtml.cs:42`, `Pages/ProjectNews.cshtml.cs:40`, `Pages/Roadmap.cshtml.cs:28`, njihovi odgovarajući `.cshtml` obrasci. Naslov od razmaka samo je siguran način dolaska do stvarne serverske greške; sama zabrana takvog naslova u pregledniku ne rješava očuvanje nacrta.

Prihvat: pri validaciji i odbijenom spremanju sva polja, razmaci, odabrana stavka i izvorna revizija ostaju sačuvani; ne prepisivati postojeću verziju niti automatski zamijeniti token novim. Upozorenje pri napuštanju djeluje i bez nove izmjene. Posebno testirati konflikt plana na izoliranoj bazi, HR/EN i relevantne providere. Dokazi: `additional.json`, `hr/en-Create-rejected.png`, `hr/en-Publish-rejected.png`, `hr/en-plan-validation.png`.

### REV3-02 — mobilne tablice

Na 320 px otvoriti `/Issues` i `/TimeReport?ProjectId=1`. EN popis zadataka ima stupce približno 24,9–36 px; prvi redak visok je **705 px**. Detalji vremena imaju stupce približno 28–36 px i prvi redak visok **465 px**. Datum i naslovi raspadaju se na gotovo pojedinačne znakove. Sličan, blaži problem postoji u korisnicima i administrativnom auditu. Dokument nema globalno prelijevanje, pa sama provjera `scrollWidth <= innerWidth` daje prolaz.

Uzrok: globalni `main{overflow-wrap:anywhere}` uz tablice bez minimalne čitljive širine (`Pages/Shared/_Layout.cshtml:35`, `Pages/TimeReport.cshtml:50`, `Pages/Issues.cshtml:36`). Prijašnji popravak `.issue-time-table` štiti tablicu vremena unutar zadatka, ali ne i ove tablice.

Prihvat: čitljivi datumi, nazivi i sati na 320/390 px, bez globalnog vodoravnog pomicanja; lokalno pomicanje dostupno tipkovnicom i označeno. Provjeriti HR/EN, teme i duge tekstove vizualno, uz axe. Dokazi: `additional.json` mjerenja i `hr/en-mobile-Issues.png`, `hr/en-mobile-TimeReport.png`.

### REV3-03 — očuvanje HTTP statusa

GET `/Calendar?Month=bad`, `/Gantt?Month=bad` i `/Forum?projectId=bad` vraćaju **404** s generičkom stranicom, iako handleri izričito vraćaju `BadRequest()` (**400**). HR i EN daju isti obrazac ponašanja.

Uzrok: `Program.cs:267` uključuje `UseStatusCodePagesWithReExecute("/NotFound")` za sve odgovarajuće prazne odgovore greške; `Pages/NotFound.cshtml.cs:9` zatim bezuvjetno postavlja 404. To je potvrđena regresija dodatne grane REV2-09. Mogući zahvat drugih praznih statusa proizlazi iz tog koda; 403/429/500 nisu posebno izazivani na demou.

Prihvat: 404 ostaje generički i privatan; 400/403/429/500 čuvaju vlastiti status i relevantna zaglavlja. API i health odgovori ostaju nepromijenjeni. Provjeriti GET/HEAD i izravni posjet stranici greške, bez izazivanja opterećenja na čistom demou. Dokaz: `additional.json`, polje `edges`.

### REV3-04 — preostali povratni tokovi

- `/Gantt?Month=2026-11&ProjectId=1` → zadatak #1; povratak je `/Issues?ProjectId=1`.
- `/Calendar?Month=2026-12&ProjectId=1` → zadatak #4; povratak je također popis zadataka, uz gubitak mjeseca.
- `/Activity?ProjectId=1` → zadatak #2; povratak ne vodi na aktivnosti.
- `/Issue?id=1` → `/TimeEntry?id=1&issueId=1`; gornja poveznica nudi povratak na `/TimeReport?ProjectId=1`, a ne na izvorni zadatak.

Uzrok: poveznice u `Pages/Gantt.cshtml:45`, `Pages/Calendar.cshtml:55`, `Pages/Activity.cshtml:41` i tablici vremena u `Pages/Issue.cshtml:165` ne prenose kontekst; `Pages/TimeEntry.cshtml.cs:18` koristi izvještaj kao zadani povratak. Raniji tokovi pretrage i izvještaja ostaju zasebno pokriveni.

Prihvat: stvarni povratak vodi na izvorni mjesec/projekt/filtre/stranicu i redak ili sidro, nakon validacije i spremanja. I dalje prihvaćati samo sigurne lokalne adrese i ne mijenjati ovlasti. Neposredno otvaranje detalja mora imati smislen zadani povratak. Dokaz: `additional.json`, polje `navigation`.

### REV3-05 — sistemski naziv grupe na EN

Na engleskom otvoriti `/Issues?GroupBy=assignee`. Caption je `Nije dodijeljen · 4 issues`, dok su ostale sistemske oznake prevedene. `IssuesModel.GroupName` vraća hrvatski literal (`Pages/Issues.cshtml.cs:96`), a caption prikazuje `group.Key` izravno (`Pages/Issues.cshtml:47`).

Prihvat: nedodijeljena grupa na EN glasi `Unassigned`, HR zadržava hrvatsku oznaku. Ne prevoditi prikazna imena osoba ni korisničke vrijednosti. Dokaz: `additional.json`, polje `groups`.

### REV3-06 — sekundarna navigacija

Na `/CustomFields?scope=project&entityId=1`, `/ApiTokens` i `/EmailNotifications` nema `aria-current=page` ni oznake nadređenog odjeljka. `/Workflow` nema aktivnu oznaku sekundarne administrativne poveznice. To nije blokada rada ni prijavljena axe povreda; predlaže se dosljednija orijentacija korisnika. Ne očekuje se aktivna navigacija na generičkoj 404 stranici.

Prihvat: projektna polja pripadaju projektima; osobni tokeni/e-pošta profilu; točna administrativna stranica ima sekundarnu oznaku. Ne označavati nevezane poveznice ili paginaciju. Lokacija: `wwwroot/js/forms.js`, postojeća mapa odjeljaka i selektor `[data-primary-navigation]`.

## Predloženi redoslijed

1. **REV3-01:** zaštititi nacrte i dodati regresije za preostale POST obrasce.
2. **REV3-03:** ispraviti statusnu regresiju i proširiti negativne HTTP provjere.
3. **REV3-02:** osigurati upotrebljive mobilne popise i izvještaje.
4. **REV3-04:** dovršiti povratne tokove planiranja i vremena.
5. **REV3-05/06:** završiti lokalizaciju grupe i navigacijske oznake.

Detaljne lokalne snimke, tekstovi, mjerenja i skripte reprodukcije ostaju ignorirani; privatni pristupni podaci i snimke baze nisu dio izvještaja ili Gita.
