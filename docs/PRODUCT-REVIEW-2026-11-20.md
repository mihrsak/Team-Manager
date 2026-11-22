# Revizija Team Managera — 20. 11. 2026.

Pregledana verzija: `28c8bb3`. Lokalni preglednik: Db2 paket na https://localhost:7589. Status nalaza: otvoreno; ovo je prijedlog dorada, a ne potvrda njihova izvršenja.

Glavni zaključak: prije korisničkog pilota prvo treba zatvoriti otkrivanje privatnih projektnih metapodataka kroz profile, zaštititi nespremljene izmjene i pojednostaviti dolazak do zadataka. HR/EN prijevodi pokrivaju većinu obrazaca, ali imaju konkretne praznine. Prolazak prethodnih automatiziranih testova nije uklonio ove probleme uporabljivosti i rubnih tokova.

Naknadni status 22. 11. 2026.: svih 14 stavki REV-01–REV-14 lokalno je implementirano i provjereno. Ovaj izvještaj čuva izvorne nalaze; aktualni dokazi su u [BACKLOG.md](BACKLOG.md), [RELEASE-STATUS.md](RELEASE-STATUS.md) i [PRODUCT-DEMO.md](PRODUCT-DEMO.md).

## Opseg i ograničenja

Ručno su pregledani prijava/odjava, projekt i projektni zadaci, uređivanje zadatka bez spremanja, filtri i skupne radnje, pretraga, kalendar/Gantt, planovi, wiki, dokumenti, forum, izvještaj vremena, profil i administracija šifrarnika. Pregledani su HR i EN prikazi te pristup postojećeg ograničenog testnog korisnika. Na demo projektu provjereni su i prazni prikazi suradničkih modula; njihovo stvaranje i izmjena sadržaja nisu ponovno izvršavani.

Reprodukcija je dopunjena pregledom izvornog koda i 578 EN resursnih ključeva. Snimke i sažeti dokaz nalaze se lokalno u ignoriranom direktoriju `artifacts/product-review-2026-11-20`. Adminu je vraćen početni HR jezik; testni naslov nije spremljen, a nevaljani prioritet nije dodan. Postojeći zadaci, članstva i ovlasti nisu mijenjani.

Prethodne integracijske, SMTP/IMAP i 52 automatizirane HR/EN provjere iz 19. 11. navedene su u [statusu izdanja](RELEASE-STATUS.md). Ova revizija nije novo izvršenje cijelog tog paketa, penetracijski test, nova NVDA provjera, mobilna provjera ni potvrda ciljnog IIS/OCP okruženja. LDAP/Git prijevodne praznine navedene u nastavku potvrđene su kodom/resursima, a ne novom prijavom na LDAP ili novim Git tokom.

## Prioriteti

- **P1:** zatvoriti prije korisničkog pilota; privatnost, gubitak rada, pristup osnovnom toku ili priprema pouzdanog demonstracijskog okruženja.
- **P2:** sljedeća iteracija; dosljednost, učinkovitost rada i jasno označena proširenja postojećeg opsega.

Nije potvrđen P0 problem potpune nedostupnosti ili pristupa sadržaju tuđih privatnih zadataka. To nije zaključak da takvi problemi ne postoje.

| ID | Prioritet | Nalaz / prijedlog | Vrsta dokaza | Kriterij prihvata dorade |
|---|---|---|---|---|
| REV-01 | P1 | Profil drugog korisnika otkriva nazive privatnih projekata i njegove članske uloge, iako promatrač te projekte ne vidi u `/Projects`. | Preglednik + kod | Poslužiteljski filtrirati projektna članstva prema ovlastima promatrača; negativni testovi na SQL Serveru, Oracleu i Db2. |
| REV-02 | P1 | Odlazak s obrasca odbacuje nespremljene izmjene bez upozorenja. | Preglednik + kod | Ponuditi ostanak/odustajanje kada je obrazac izmijenjen; spremanje ne smije izazivati lažno upozorenje. |
| REV-03 | P1 | Na filtriranom popisu zadataka rezultati se nalaze nakon popisa svih dostupnih projekata, spremljenih upita i skupnog uređivanja. | Preglednik + kod | Za odabrani projekt odmah prikazati njegove zadatke i jasno istaknutu radnju stvaranja; ostale projekte premjestiti u pretraživ odabir. |
| REV-04 | P1 | Nakon neuspjelog spremanja šifrarnika fokus ostaje na `BODY`; sažetak pogreške nema upozoravajuću ulogu ni fokus. | Preglednik + DOM | Nakon greške fokusirati sažetak ili nevaljano polje, povezati poruke s poljima i provjeriti najavu čitačem zaslona u HR/EN. |
| REV-05 | P2 | Nepotpuna lokalizacija sistemskih oznaka: `created`, naslov `Projekti` u EN profilu, `Nije dodijeljena`, nedostajući EN ključevi `Autor` i `Natrag na prijavu`. | Preglednik + kod/resursi | Prevesti statične oznake i vrste događaja; testirati vidljivi tekst, ne samo `html lang`. |
| REV-06 | P2 | EN sučelje zadržava fiksne `dd.MM.yyyy` datume i brojčani format okoline, npr. `Total 89,50 h`. | Preglednik + kod | Dogovoriti regionalni format odvojeno od jezika; dosljedno formatirati datume, sate i vremensku zonu, uz iste spremljene vrijednosti. |
| REV-07 | P2 | Povratak sa zadatka vodi na opći `/Issues`, bez projekta, filtara i spremljenog prikaza. | Preglednik + kod | Povratak čuva prethodni projekt, filtre i prikaz; lokalni povratni URL mora biti validiran. |
| REV-08 | P2 | Polje naziva šifrarnika dopušta 50 znakova za sve vrste, a poslužitelj dopušta 20 za prioritet, 30 za status i 50 za tip. Poruka ne navodi konkretno ograničenje. | Preglednik + kod | Uskladiti ograničenja i pomoćni tekst s odabranom vrstom te prikazati točan razlog odbijanja u HR/EN. |
| REV-09 | P2 | Prioriteti i statusi sortirani su po nazivu, a demo ima paralelne nazive i semantičko miješanje, npr. `Otkazano` i `Blokirano` među prioritetima. | Preglednik + kod; prijedlog modela | Dogovoriti poredak prioriteta i razdvajanje stanja/prioriteta; omogućiti rangiranje i deaktiviranje uz očuvanje starih zadataka. |
| REV-10 | P2 | „Svi moduli” pretražuje samo projekte, zadatke, planove i korisnike. Wiki, dokumenti, novosti i forum nisu uključeni; nakon 20 rezultata nema nastavka prikaza. | Preglednik + kod; proširenje opsega | Jasno navesti opseg ili dodati module uz provjeru ovlasti te straničenje/„više rezultata”. |
| REV-11 | P2 | Navigacija ne ističe trenutačnu stranicu; administrativne poveznice vizualno se stapaju, a sve kartice imaju naslov `Team Manager`. Projektni detalj miješa pregled i velik obrazac uređivanja. | Vizualni pregled + kod; UX prijedlog | Uvesti kontekst projekta, aktivnu stavku, zasebnu administraciju i opisne naslove kartica; pregled projekta odvojiti od postavki. |
| REV-12 | P1 | Demonstracijsko okruženje pomiješano je s regresijskim podacima: 345 projekata, brojni tehnički nazivi, a glavni demo nema planove/wiki/dokumente/forum za smislen pregled. | Preglednik | Zasebna ponovljiva demo baza s nekoliko razumljivih projekata i reprezentativnim sadržajem; regresije rade u drugim shemama/bazama. Bez brisanja korisničkih podataka. |
| REV-13 | P2 | Evidencija vremena nudi unos i pregled, ali nema korisničkog ispravka ili storniranja pogrešno unesenih sati. | Preglednik + kod; proširenje TIME-01 | Omogućiti kontrolirani ispravak/storno uz autorstvo, ovlasti i revizijski trag; izvještaj uključuje ispravak jednom. |
| REV-14 | P2 | Obavijesti i administrativna revizija imaju ograničene nedavne popise, bez pregleda starijih stranica; izvještaju vremena nedostaje detaljni prikaz pojedinog unosa. | Kod; operativno proširenje | Dodati filtre, straničenje i ovlašteni prijelaz na detalje; jasno prikazati opseg rezultata. |

## Reprodukcija pet P1 nalaza

### REV-01 — otkrivanje privatnih projektnih metapodataka

1. Prijaviti se postojećim ograničenim lokalnim testnim računom bez članstva u projektu `Demo • Team Manager`.
2. Otvoriti `/Projects`: taj projekt nije na popisu; prikazuju se dostupni javni projekti.
3. Iz imenika korisnika otvoriti profil administratora, `/Profile?id=1`.
4. Profil prikazuje `Demo • Team Manager`, druge privatne projekte i uloge poput `Voditelj`.
5. Izravan pristup `/Issue?id=10` i dalje završava s HTTP 404. Potvrđen je problem metapodataka, ne čitanje sadržaja zadatka.

Uzrok: [ProfileModel](../src/TeamManager.Web/Pages/Profile.cshtml.cs#L32) dohvaća članstva odabranog korisnika bez filtriranja prema trenutačnom promatraču. [UserStore.Memberships](../src/TeamManager.Web/UserStore.cs#L95) i [relacijski dohvat](../src/TeamManager.Web/OracleWorkspace.Accounts.cs#L31) primaju korisnika čija se članstva prikazuju, a ne kontekst ovlasti promatrača. Popravak treba provesti u poslužiteljskom dohvaćanju, ne samo skrivanjem HTML-a. Vidljivost vlastitih članstava i administratorski pregled treba sačuvati prema dogovorenoj politici.

### REV-02 — gubitak nespremljene izmjene

Na `/Issue?id=10` promijeniti naslov, zatim kliknuti povratak na projektne zadatke ili profil. Nema upozorenja. Ponovnim otvaranjem zadatka vraća se spremljeni naslov `Pregledati prava članova projekta`. Izmjena nije spremljena i nije stvoren novi zapis zadatka. Pri doradi obuhvatiti i opis, komentare, wiki i druge višepoljne obrasce; autorove nacrte ne izlagati drugim korisnicima.

### REV-03 — zadaci iza popisa projekata

Otvoriti `/Issues?ProjectId=2`: postoje samo tri demo zadatka, ali prije njih se iscrtava popis svih dostupnih projekata s ponovljenom radnjom „Novi zadatak”. `/Projects` administratoru prikazuje 345 projekata bez filtra ili straničenja. [Issues.cshtml](../src/TeamManager.Web/Pages/Issues.cshtml#L30) bezuvjetno iscrtava `Model.Projects` ispred rezultata. Test prihvata treba uključiti stotine projekata i tipkovnicu, ne samo mali početni skup.

### REV-04 — pogreška bez usmjerenog fokusa

U EN `/Workflow`, u obrascu nove vrijednosti odabrati `Priority` i unijeti 22 znaka `REVIEW_PRIORITY_123456`. Obrazac dopušta unos, a poslužitelj ga odbija porukom `The name is invalid or already exists.` Na vraćenoj stranici `document.activeElement` je `BODY`; sažetak nema `role` ni `tabindex`. Prioritet nije dodan. To ne dokazuje ponašanje svakog čitača zaslona, ali potvrđuje nedostatak programskog usmjeravanja i traži ručni test greške. Prijava već ima posebnu obradu fokusa; isti obrazac treba dosljedno primijeniti na ostale forme.

### REV-12 — demonstracijski podaci

Na prvom ekranu projekata dominiraju `Branch A`, `Fine locks`, `Oracle ...` i drugi regresijski nazivi. U projektu `Demo • Team Manager` postoje tri razumljiva zadatka, ali planovi, wiki, dokumenti i forum prazni su. Za novu demonstracijsku bazu pripremiti voditelja, suradnika i promatrača, privatni projekt, verziju s napretkom, nekoliko zadataka, wiki s dvije verzije, dokument, temu s odgovorom te vremenske zapise. Regresijski podaci ostaju odvojeni; ova revizija ih nije uklanjala.

## HR/EN i prihvat novih dorada

Vidljiva kontrola potvrđuje prijevode glavne navigacije, osnovnog obrasca zadatka, kalendara i suradničkih obrazaca. Pretraga `Demo` vraća rezultate i navodi ograničenje od 20 po modulu. Nije uočeno pogrešno prevođenje spremljenog korisničkog sadržaja; hrvatski nazivi zadataka, statusa i rola sami po sebi nisu dokaz neprevedenog sistemskog teksta.

Za REV-05 konkretne lokacije su [profil](../src/TeamManager.Web/Pages/Profile.cshtml#L71), [zadana organizacijska jedinica](../src/TeamManager.Web/Pages/Profile.cshtml.cs#L25), [povijest zadatka](../src/TeamManager.Web/Pages/Issue.cshtml#L166), [Git tablica](../src/TeamManager.Web/Pages/RepositoryHistory.cshtml#L35) i [LDAP povratak](../src/TeamManager.Web/Pages/LdapLogin.cshtml#L16). Statička provjera našla je i ključ `Commit` bez zasebnog EN unosa; isti naziv u oba jezika nije zaseban kvar. Provjera literalnih `Text[...]` ključeva ne može otkriti sve zaobiđene prijevode, poput izravnog stringa `Projekti`.

Za REV-06 middleware postavlja `CurrentUICulture`, ali ne i `CurrentCulture`; dio predložaka dodatno koristi fiksni datum. Treba definirati politiku regionalnih postavki, ne samo zamijeniti jedan format. UTC prikaz je označen i nije potvrđena pogreška spremljene vremenske vrijednosti.

[Postojeći browser test](../scripts/Test-Browser.cjs#L38) provjerava jezični atribut i automatizirana pravila pristupačnosti, što ne jamči da je svaki vidljivi tekst preveden. Dodati prihvatne scenarije za čitatelja tuđeg profila, stvarne EN oznake, neuspjelo spremanje, nespremljene izmjene i velik broj projekata. Prolazak na valjanoj stranici nije zamjena za provjeru stanja nakon pogreške.

## Predloženi redoslijed dorada

1. **Privatnost:** REV-01 i regresijski testovi svih providera, HR/EN, vlastitog i tuđeg profila.
2. **Pouzdanost unosa:** REV-02 i REV-04; uz njih mali popravak ograničenja iz REV-08.
3. **Svakodnevni rad i demo:** REV-03, REV-07 i REV-12. Tek nakon toga novi korisnički obilazak proizvoda.
4. **Dosljednost HR/EN:** REV-05 i REV-06, uključujući pogreške i čitač zaslona.
5. **Proširenja prema stvarnoj uporabi:** REV-09, REV-10, REV-11, REV-13 i REV-14.

Otvorene ciljne stavke iz postojećeg backloga — bankarski SSO/AD, stvarni sandučić, OpenShift/produkcijski IIS, kapacitet i Db2 z/OS s licencom — ostaju zasebni uvjeti prihvata. Funkcionalni opseg označen „gotovo” u starom backlogu ne znači da su ovdje pronađene dorade već zatvorene.
