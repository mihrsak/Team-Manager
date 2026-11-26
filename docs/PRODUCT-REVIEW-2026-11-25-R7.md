# Sedma revizija Team Managera — 25. 11. 2026.

Nova revizija započela je na commitu **0f1cef4**, na stvarnom [lokalnom IIS-u](https://localhost:7443). Pronađena su i popravljena **tri nalaza: jedan P1 i dva P2**. Pregled i dorade provedeni su na HR/EN sučelju uz vlastite označene testne zapise, bez resetiranja postojeće baze ili konfiguracije.

| Stavka | Prioritet | Nalaz i završno ponašanje |
|---|---|---|
| REV7-01 | P1 | Vraćeni nacrt projekta nije imao aktivnu zaštitu odlaska/reloada ni postupak pregleda nakon sukoba. Sada ostaju sirovi tekst, izvorna revizija i zaštita; prikazana je spremljena verzija, a preuzimanje nove revizije zahtijeva potvrdu pregleda. |
| REV7-02 | P2 | Nakon sukoba postavke modula vraćale su se na spremljene vrijednosti uz novu reviziju. Sada ostaju izvorni odabiri i revizija, uz izričit pregled, ponovno odbijanje drugog sukoba i siguran oporavak. Nevaljano vezani parametri odbijaju se prije upisa. |
| REV7-03 | P2 | Administratorski obrazac projekta ponovno je dohvaćao iste globalne ovlasti za svakog mogućeg roditelja. Sada se puna administracija provjerava u bazi bez tog niza upita; ostalim korisnicima ostaju pojedinačne provjere. |

## Reprodukcije i popravci

### REV7-01 — nacrt projekta i kontekst spremljene verzije

U postojećem projektu upisati naziv od razmaka i opis s početnim praznim retcima, zatim poslati. Izvorni IIS odgovor čuvao je tekst, ali nije imao oznaku vraćenog nacrta, a `beforeunload` zaštita bila je neaktivna. Pri konkurentnoj izmjeni izvorna revizija ostajala je zastarjela bez mogućnosti izričitog oporavka. Korisnik je morao ponovno otvarati stranicu i ručno prenositi tekst. Nevaljan naziv mogao je isprazniti i glavni naslov postojećeg projekta.

Popravak zadržava tekst i pokušavanu reviziju, označava vraćeni nacrt i aktivira postojeće upozorenje. Spremljeni naziv, opis, stanje, vidljivost, roditelj i nasljeđivanje prikazani su odvojeno od obrasca. Za ponovni pokušaj nakon sukoba potrebno je pregledati aktualnu verziju i potvrditi zamjenu; drugi sukob ponovno odbija upis i čuva nacrt. Naslov stranice ostaje naziv spremljenog projekta. Poruka o aktualnoj spremljenoj stavci prikazuje se samo kada stvarno postoji sukob, uključujući ispravan tok novog projekta.

Ponovno učitavanje izbora roditelja ne gomila duplikate. Nedostupni roditelj dobiva HR/EN oznaku bez privatnog naziva; stvarni korisnički nazivi ne prevode se prema svojem sadržaju.

### REV7-02 — odabiri modula nakon sukoba

Urednik A isključi zadatke i vrijeme, B u međuvremenu promijeni projekt, zatim A pošalje ranije otvoren obrazac. Izvorni odgovor vraćao je oba uključena modula i novu reviziju, umjesto A-ove namjere. Na tom vraćenom obrascu nije bilo aktivne zaštite nacrta.

Sada obrazac čuva oba odabira i pokušavanu reviziju, prikazuje aktualne vrijednosti te traži izričit pregled prije zamjene. Provjeren je drugi sukob prije uspješnog oporavka. Provjera vezanih parametara prethodi čišćenju ModelStatea; nevaljana revizija daje 400 i ne mijenja module. Pohrana i dalje koristi postojeće provjere prava i zaključavanje.

### REV7-03 — ponovljene provjere pune administracije

Obrazac je za svaki kandidat nadređenog projekta pozivao provjeru upravljanja. SQL popis projekata također je pojedinačno provjeravao sve retke čak i za punog administratora. Oracle probna shema pokazala je i predaju koja nije završila unutar početnog čekanja testa od 15 sekundi.

Nova provjera čita stvarnu globalnu ovlast `*` iz baze, uključujući aktivnost računa; ne oslanja se na stari cookie ili oznaku da korisnik može stvarati projekte. Puni administrator dobiva isti dopušteni skup bez ponavljanja iste provjere po projektu. Ostali korisnici prolaze dosadašnje pojedinačne provjere. Upisi ponovno provjeravaju prava i revizije. Nema nove sheme, promjene nasljeđivanja ovlasti ili trajnog cachea prava.

## Provjere

- Početni i završni pregled na IIS-u: po **82 HR/EN prikaza**, desktop 1440 px i mobilnih 320 px, s axe WCAG 2 A/AA i 2.1 A/AA provjerama. Obuhvaćeni su projekti/zadaci, novi zadatak, grupiranje, pretraga, kalendar/Gantt, aktivnosti, planovi, wiki, dokumenti, novosti, forum, repozitoriji, vrijeme, obavijesti, audit, korisnici/profil, role/grupe, katalog polja, osobne postavke i stranice grešaka.
- `Test-ProjectDrafts.cjs` na IIS-u te izoliranim SQL Server/Oracle/Db2 bazama: HR/EN, sirovi tekst i početni prazni retci, smislen H1 nakon greške, izvorne revizije, odbijen odlazak i stvarni reload, dva sukoba projekta i modula, potvrđeni oporavak, HTML kodiranje, jedinstveni izbori roditelja, axe i nevaljano vezana revizija bez upisa.
- Ograničeni projektni voditelj vidi englesku oznaku nedostupnog privatnog roditelja bez njegova naziva. Nakon uklanjanja članstva otvoreni projektni obrazac daje 404 bez vraćanja privatnog nacrta. Time je provjeren i korisnik koji ne smije koristiti administratorski brži put.
- `Test-IisLocalAcceptance.cjs`: pozitivni HR/EN tokovi projekt/zadatak/komentar/vrijeme/pretraga/CSV, prazan neobvezni opis, mobilni izvještaj, lokalizirani 404 i axe. `Test-FieldBulkDrafts.cjs` ponovno provjerava prethodne nacrte polja, skupne radnje i opoziv prava na IIS-u.
- Dotnet demo i radni Db2 pregled ažurirani su istim aplikacijskim kodom; prolaze `Test-ProductDemo.cjs` i `Test-ReviewSmoke.cjs`. Demo zadržava tri projekta, osam zadataka i povezani sadržaj. Jezik i tema vraćeni su na početne postavke.

Završni pregled ima **0 axe prijava, 0 prelijevanja cijelog dokumenta i 0 JS grešaka** u navedenih 82 prikaza. Vizualno su provjereni mobilni EN projekt i HR izvještaj vremena. Potvrđeni novi nalazi zatvoreni su; u ponovljenom pregledu nema dodatnih reproduciranih nalaza u tom opsegu.

Kratko lokalno IIS mjerenje šest autentificiranih ruta, po 10 sekundi za razine 1/10/25, završilo je s **1606 odgovora bez HTTP grešaka**. Pri 25 paralelnih zahtjeva medijan je 310 ms, p95 448 ms. To je zaseban lokalni uzorak, bez tvrdnje o usporedivom benchmarku prije/poslije ili kapacitetu ciljne produkcije.

Konačni IIS paket je `artifacts/review7-final`; nadogradnja koristi verificirani COPY_ONLY/CHECKSUM backup, postojeću konfiguraciju i provjeru hasha/readinessa. SQL shema ostaje 43, Oracle/Db2 shema 11 i protokol zaključavanja 5. Dotnet pregledi koriste `artifacts/review7-current-preview` i ostaju odvojeni od namjenske IIS SQL testne baze.

Nova NVDA sesija nije dio ove revizije; prethodni stvarni IIS/NVDA prihvat ostaje zaseban dokaz u [LOCAL-IIS-NVDA-2026-11-25.md](LOCAL-IIS-NVDA-2026-11-25.md). Ovo nije formalna potvrda potpune WCAG usklađenosti, produljeni kapacitet, nova obnova baze ili provjera ciljnog bankarskog IIS-a/OpenShifta.

## Dokazi

Lokalno, ignorirano: `artifacts/product-review7-2026-11-25`, `artifacts/product-review7-final-2026-11-25`, `review7-project-baseline.log`, zasebni `review7-release-*.log`, `review7-iis-*.log` i manifesti IIS objave. Pohranjene su tekstualne snimke i mobilne slike odabranih glavnih tokova. Verzija regresije projekta je u `scripts/Test-ProjectDrafts.cjs`; sesijski podaci, lokalne konfiguracije i pristupne tajne nisu dio commita.

Sva tri potvrđena nalaza lokalno su zatvorena. Glavni pregled: [https://localhost:7443](https://localhost:7443).
