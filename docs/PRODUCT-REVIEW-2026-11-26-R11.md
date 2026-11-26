# Jedanaesta revizija Team Managera — 26. 11. 2026.

Revizija je započela na commitu **5765274**, na stvarnom [lokalnom IIS-u](https://localhost:7443). Početni pregled 82 HR/EN prikaza nije našao nove axe prijave, prelijevanje dokumenta ni JavaScript greške. Dodatne provjere obrazaca i listi potvrdile su četiri nalaza: tri P2 i jedan P3.

| Stavka | Prioritet | Nalaz i popravak |
|---|---|---|
| REV11-01 | P2 | Odbijena validacija profila vraćala je spremljeno ime, funkciju i jedinicu umjesto poslanih unosa. Sada netajni sirovi tekst i odabir ostaju u svom obrascu uz zaštitu odlaska; spremljeni identitet ostaje u naslovu. |
| REV11-02 | P2 | Duplikat korisnika praznio je employee/name/job/unit; duplikat organizacijskog naziva gubio je naziv i vraćao vrstu na jedinicu. Sada oba obrasca imaju odvojene nacrte i odabire, a lozinka ostaje prazna. |
| REV11-03 | P2 | Obavijesti i revizijski zapis ignorirali su nevaljane/ponovljene filtre te tiho skraćivali upit. Sada se filtri provjeravaju prije dohvata; nevaljani Read zahtjev ne označava obavijest. |
| REV11-04 | P3 | Hrvatska stranica ovlasti govorila je da implementirani moduli tek dolaze i prikazivala tipfeler „Plna administracija”. Sada prikazuje aktualnu uputu i „Puna administracija”, uz odgovarajući EN tekst. |

## Reprodukcije

Oracle R10 baseline potvrdio je HR/EN gubitke u obrascima: profil s imenom od razmaka i novom funkcijom vraćao je spremljenu funkciju; duplikat QA korisnika praznio je netajne unose i odabir jedinice; duplikati jedinice/tima gubili su naziv i odabranu vrstu. Vraćeni obrasci nisu imali aktivnu zaštitu nacrta. Lozinka je već bila ispravno prazna i to ponašanje je očuvano. Dokaz: artifacts/review11-account-drafts-baseline.log.

Db2 R10 baseline potvrdio je da nevaljani broj stranice, ponovljeni filtri i broj stranice 0 prikazuju rezultate bez poruke, a upit od 201 znaka nema validacijski sažetak. Nevaljani PageNumber u POST Read također je označio vlastitu QA obavijest. Dokaz: artifacts/review11-notification-filters-before.log. Ovo nije potvrda otkrivanja tuđih podataka, nego neispravne validacije i neočekivanog upisa.

IIS baseline na oba jezika potvrdio je da su tipfeler i zastarjela uputa prisutni na HR prikazu; EN uputa je već bila aktualna. Dokaz: artifacts/review11-role-guidance-before.log.

## Implementacija

Nacrti profila, stvaranja korisnika i organizacijskog naziva odvojeni su zastavicama po handleru. Čuvaju se samo netajni POST unosi nakon provjere prava. Nepostojeća jedinica dobiva postojeću generičku HR/EN oznaku za ponovni odabir, bez zamjene drugom jedinicom. Razor kodira tekst. Lozinka se ne vraća u value, skriveno polje ni pohranu nacrta. Odbijanje nepovezanih radnji računa/lozinke ne stvara nacrt osnovnih podataka; neovlašteni POST vraća 403 bez privatnog nacrta.

Obavijesti i revizijski zapis koriste StringLength200, ModelState i SingleValues prije upita; broj stranice provjerava se u dopuštenom rasponu. Izvorni Query ostaje u obrascu. Greška ima lokaliziran sažetak, bez rezultata, paginacije ili lažne poruke o praznom popisu. Nevaljani POST Read vraća validacijsku stranicu bez poziva MarkRead; valjani filtri i označavanje zadržavaju kontekst upita.

Opis ovlasti promijenjen je samo u tekstu prikaza. Interna ovlast * i transakcijske provjere nisu mijenjane. Dodana su dva EN resursa za aktualnu HR formulaciju. Ostali obrasci i validacije koriste postojeće HR/EN ključeve. Sheme baze i protokol zaključavanja nisu mijenjani.

## Provjere i dokazi

- Test-AccountDrafts.cjs: HR/EN sirovi unosi i HTML kodiranje, odvojeni obrasci, jedinice i generički nevaljan odabir, prazne lozinke, zaštita nacrta, ispravak, stabilan naslov, nepovezani password/account handleri, zabrana neovlaštenog upisa bez prikaza nacrta i axe.
- Test-NotificationAuditFilters.cjs: HR/EN nevaljani, ponovljeni, predugi i izvanrasponski filtri; lokalizirane poruke, izvorni Query, bez rezultata/upisa nakon greške, valjan filtar i označavanje te admin403/opoziv pristupa i axe.
- Test-RoleGuidance.cjs: HR/EN naziv pune administracije i aktualna uputa.
- Test-Review11Sql.cjs povezuje nove provjere s prethodnim kontroliranim testom opoziva ovlasti. U ovom izvršavanju account test je zasebno ponovljen nakon ispravka testnog očekivanja neaktivnog data-* atributa; source se nije mijenjao.

Nove funkcionalne HR/EN provjere prošle su na izoliranim SQL Server, Oracle i Db2 bazama. Šest kontroliranih race slučajeva iz R10 provjereno je samo na privremenom SQL Serveru i i dalje odbija upis nakon opoziva/deaktivacije. Dokazi: artifacts/review11-sql.log, artifacts/review11-sql-account-drafts.log, artifacts/review11-oracle-test.log i artifacts/review11-db2-test.log. Db2 log čuva i prvotni neuspjeli testni null-assert; završno ponavljanje potvrđuje PASS. Neaktivni Razor data-* atribut može biti prazan, a test sada provjerava da nije true; zaštita aktivnog nacrta i dalje zahtijeva točno true.

Paket: artifacts/review11-final. Završni IIS prihvat potvrđen je ispod. Početnih 82 prikaza odnosi se na prethodni paket.

NVDA, opterećenje i ciljno produkcijsko okruženje nisu ponovno provjeravani. Čisti demo 7610 i postojeći pregled 7589 nisu korišteni za testne poslovne upise. Pregled ne potvrđuje odsutnost svih mogućih grešaka.

## Završna IIS objava i prihvat

Nakon Windows UAC potvrde paket je objavljen iz direktorija C:\inetpub\TeamManager-LocalTest\review-20261126-090242 na https://localhost:7443. Očuvani su postojeća testna baza i konfiguracija; verificirani su COPY_ONLY backup, hash paketa i HTTPS readiness 200. Manifest: artifacts/review11-iis-update.json.

Na novom IIS paketu prošli su HR/EN testovi:

- Test-AccountDrafts.cjs: nacrti profila/korisnika/organizacije, prazne lozinke, odvojeni handleri, odabiri jedinica, oporavak, autorizacija i axe. Dokaz: artifacts/review11-iis-account-drafts.log.
- Test-NotificationAuditFilters.cjs: pogrešni, ponovljeni, predugi i izvanrasponski filtri bez rezultata ili mutacije; valjani filtri/Read, ovlasti i axe. Dokaz: artifacts/review11-iis-filters.log.
- Test-RoleGuidance.cjs: aktualna uputa i naziv pune administracije. Dokaz: artifacts/review11-iis-role-guidance.log.
- Test-IisLocalAcceptance.cjs: glavni tokovi projekata/zadataka/komentara/vremena/pretrage/CSV, 404 i mobilni izvještaj uz axe. Dokaz: artifacts/review11-iis-acceptance.log.
- Test-AdministrationValidation.cjs: prethodne validacije duplikata organizacije i nepostojeće jedinice, očuvani podaci i valjan ispravak. Dokaz: artifacts/review11-iis-administration.log.

Ponovljeni pregled novog IIS paketa prošao je 82 HR/EN prikaza uz očekivane HTTP statuse: nema novih axe prijava, prelijevanja dokumenta ni JavaScript grešaka. Dokazi: artifacts/review11-overview-final.log i artifacts/product-review11-final-2026-11-26/overview.json. Kontrolirani SQL race test nije izvođen nad IIS bazom. Sva četiri nalaza zatvorena su u provjerenom lokalnom opsegu.
