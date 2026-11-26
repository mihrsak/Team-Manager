# Deseta revizija Team Managera — 26. 11. 2026.

Revizija je započela na commitu **a2cb1ca**, na stvarnom [lokalnom IIS-u](https://localhost:7443). Početni pregled 82 HR/EN prikaza nije našao nove axe prijave, prelijevanje dokumenta ili JavaScript greške. Dodatne provjere odbijenih upisa i konkurentnih promjena potvrdile su pet nalaza.

| Stavka | Prioritet | Nalaz i popravak |
|---|---|---|
| REV10-01 | P1 | SQL Server spremanje i dodjela globalnih rola nisu ponovno provjeravali izvršitelja nakon čekanja zaključavanja. Sada se pod postojećim zaključavanjem provjeravaju aktivnost računa i efektivna globalna ovlast roles.manage ili *. Odbijanje prethodi upisu i auditu. |
| REV10-02 | P2 | Oracle/Db2 promjena profila s nepostojećom organizacijskom jedinicom vraćala je 500. Sada vraća postojeću lokaliziranu validaciju uz očuvane spremljene podatke. |
| REV10-03 | P2 | Oracle/Db2 stvaranje duplikata organizacijske jedinice ili tima vraćalo je 500. Sada vraća postojeću HR/EN validaciju, bez dodatnog zapisa. |
| REV10-04 | P2 | Odbijena HTTP adresa ili duplikat naziva repozitorija praznili su oba polja. Sada naziv i URL ostaju u vraćenom nacrtu uz aktivnu zaštitu odlaska. |
| REV10-05 | P2 | Odbijena kombinacija adrese i postavki e-pošte vraćala je spremljene vrijednosti umjesto korisnikovih odabira. Sada ostaju poslana adresa i oba odabira uz zaštitu nacrta. |

## Reprodukcije

**Utrka ovlasti:** na privremenoj bazi sa slučajnim nazivom TeamManager_AccountTest_<guid> QA koordinator zadržava postojeći TeamManager.Roles applock. HTTP zahtjev izvršitelja s testnom roles.manage rolom pokrene se prije opoziva. DMV provjera potvrđuje da je zahtjev doista blokiran iza QA veze; tek tada ta veza oduzima njegovu testnu rolu ili deaktivira njegov testni račun i otpušta zaključavanje. Izvorni paket nakon otpuštanja stvorio je testnu rolu ili dodijelio testnu globalnu rolu. Završni odgovor sam po sebi nije dokaz sigurnosti: nakon revokacije spremanje role moglo je već biti izvršeno prije nego što je slijedni GET odbijen. Test zato provjerava i stvarno odsustvo promjene.

Popravak ponovno provjerava izvršitelja unutar iste transakcije, nakon stjecanja mutexa i prije mijenjanja role, ovlasti, dodjele ili audita. Zadržana je zaštita posljednjeg administratora. Page handleri obrađuju UnauthorizedAccessException kao Forbid. Oracle i Db2 već su imali tu provjeru u svojim transakcijskim zapisima; njihove ispravne role nisu prepisivane novim SQL putem. Sheme i protokol zaključavanja nisu mijenjani.

**Administrativna validacija:** Oracle R9 baseline potvrdio je 500 za duplikat jedinice, duplikat tima i profil vlastitog QA korisnika uz unit=2147483647 na oba jezika. Nepostojeća jedinica nije mijenjala spremljene podatke, ali je korisnik dobivao opću grešku umjesto validacije. Sada se očekivani ArgumentException obrađuje, a izgubljene ovlasti vraćaju Forbid. SQL Server zadržava svoju postojeću FK/duplikat obradu.

**Nacrti:** Db2 R9 baseline potvrdio je gubitak oba repozitorijska polja nakon HTTP URL-a i duplikata te vraćanje spremljenih checkboxa nakon slanja prazne adrese uz uključene obavijesti. Nacrti se sada prikazuju samo u pripadajućim obrascima. Repozitorijski nacrt puni se tek nakon provjere projektnog pristupa i uređivanja. Razor kodira tekst; e-pošta prikazuje isključivo nacrt prijavljenog korisnika. Postojeće HR/EN poruke koriste se bez novih neprijevodnih oznaka.

## Testovi i lokalni dokazi

- Test-RoleRevocationRace.cjs i Invoke-RoleRevocationRace.ps1: deterministički SQL test s potvrđenim čekanjem, opozivom role za spremanje i dodjelu te deaktivacijom izvršitelja. Helper prihvaća samo generiranu privremenu SQL bazu i mijenja samo točno jedan vlastiti QA zapis. HR/EN: šest odbijenih upisa nakon popravka.
- Test-AdministrationValidation.cjs: HR/EN lokalizirane poruke, nepostojanje duplikata, očuvan profil nakon odbijanja i uspješna valjana korekcija, uz axe.
- Test-RepositoryEmailDrafts.cjs: HR/EN nevaljan HTTPS i duplikat, sirovi nazivi/URL/adresa, kodiranje, checkbox odabiri, zaštita odlaska i otkazana navigacija, uspješan ispravak i reload, axe i opoziv članstva s 404 bez privatnog nacrta. Koristi poseban nov QA račun i njegove postavke e-pošte, ne mijenja postojeće adminove postavke.
- Test-Review10Sql.cjs povezuje nove regresije i prethodni Test-TimeAdminRecovery.cjs u jednu izoliranu SQL provjeru.

Baseline artefakti: artifacts/review10-role-race-before.log, artifacts/review10-administration-baseline.log, artifacts/review10-repository-email-observe.log. Paket: artifacts/review10-final. Završni dokazi i potvrđeni status objave navedeni su ispod.

Ovaj pregled ne potvrđuje odsutnost svih mogućih grešaka. NVDA, opterećenje i ciljno produkcijsko okruženje nisu ponovno provjeravani u ovom krugu. Čisti demo 7610 i postojeći pregled 7589 nisu korišteni za testne poslovne upise.

## Rezultati izoliranih provjera i status objave

Administrativna validacija i nacrti repozitorija/e-pošte prošli su HR/EN na izoliranim SQL Server, Oracle i Db2 bazama. Šest kontroliranih race slučajeva provedeno je zasebno samo na izoliranom SQL Serveru. SQL paket uključuje i prethodne R9 provjere vremena i nevaljanih administratorskih parametara. Dokazi: artifacts/review10-sql.log, artifacts/review10-oracle-test.log i artifacts/review10-db2-test.log. Neovisni završni pregled guarda i race koordinatora nije našao konkretnu regresiju u provjerenim scenarijima.

Nakon ponovljenog Windows UAC zahtjeva objava je uspješno dovršena. Novi paket radi na https://localhost:7443 iz direktorija C:\inetpub\TeamManager-LocalTest\review-20261126-084413. Zadržani su postojeća testna baza i konfiguracija; verificirani su COPY_ONLY backup, hash paketa i HTTPS readiness 200. Manifest: artifacts/review10-iis-update.json. Raniji prekinuti UAC pokušaj nije zamijenio IIS paket; završni prihvat odnosi se na ovu novu objavu.

Na stvarnom IIS-u prošle su ove HR/EN provjere:

- Test-AdministrationValidation.cjs: duplikati jedinica/timova, nepostojeća jedinica profila, očuvani podaci, valjan ispravak i axe. Dokaz: artifacts/review10-iis-administration.log.
- Test-RepositoryEmailDrafts.cjs: vraćeni naziv/URL, odabiri i adresa e-pošte, zaštita nacrta, oporavak, kodiranje, axe i opoziv pristupa. Dokaz: artifacts/review10-iis-drafts.log.
- Test-IisLocalAcceptance.cjs: projekti, zadaci, komentari, vrijeme, pretraga/CSV, lokalizirani 404, mobilni izvještaj i axe. Dokaz: artifacts/review10-iis-acceptance.log.
- Test-TimeAdminRecovery.cjs: ponovljeni sukobi vremena, oporavak i odbijene nevaljane administratorske radnje bez izmjene podataka; 403 i opoziv pristupa. Dokaz: artifacts/review10-iis-time-admin.log.
- Test-IssueDraftRecovery.cjs: prethodni popravci nacrta zadatka, komentara i privatne bilješke, ponovljeni sukob i eksplicitni oporavak. Dokaz: artifacts/review10-iis-issue-drafts.log.

Kontrolirani race test nije izvođen nad IIS bazom. IIS prihvat obuhvaća zasebne funkcionalne HR/EN provjere novog paketa.

Ponovljeni pregled novog IIS paketa prošao je 82 HR/EN prikaza, uz provjeru očekivanih HTTP statusa: nema novih axe prijava, prelijevanja dokumenta ni JavaScript grešaka. To je završni pregled nove objave, odvojen od početnog pregleda prethodnog paketa. Dokazi: artifacts/review10-overview-final.log i artifacts/product-review10-final-2026-11-26/overview.json. REV10-01–05 zatvoreni su u provjerenom lokalnom opsegu.
