# Dvanaesta revizija Team Managera — 26. 11. 2026.

Revizija je započela na commitu **ea90f40** na stvarnom [lokalnom IIS-u](https://localhost:7443). Početni pregled 82 HR/EN prikaza nije prijavio nove axe povrede, prelijevanje dokumenta ni JavaScript greške. Fokus dodatne revizije bili su role, tijek rada, registracija i zaštita posljednjeg administratora.

| Stavka | Prioritet | Nalaz i popravak |
|---|---|---|
| REV12-01 | P1 | SQL odluka o registraciji nakon čekanja zaključavanja nije ponovno provjeravala administratora. Sada se aktivnost i efektivni globalni * provjeravaju prije stvaranja računa, brisanja zahtjeva i audita; gubitak prava vraća Forbid. |
| REV12-02 | P2 | Oracle/Db2 zaštita posljednjeg grupnog administratora ispravno je rollbackala uklanjanje, ali handler je vraćao 500. Sada poznata poslovna greška vraća postojeću HR/EN validaciju. |
| REV12-03 | P2 | Odbijeno stvaranje/uređivanje role gubilo je naziv, opis i označene ovlasti. Sada ostaju u odvojenom vraćenom nacrtu uz zaštitu odlaska. |
| REV12-04 | P2 | Odbijene vrijednosti, prijelazi i postavke kataloga vraćali su zadane odabire. Sada odgovarajući obrazac/retak čuva sirovi naziv, kind/closed, role/type/from/to i order/active. |

## Reprodukcije i implementacija

Registracijski baseline na privremenoj SQL bazi potvrdio je četiri HR slučaja (approve/reject × opoziv role/deaktivacija): zahtjev nakon potvrđenog čekanja zaključa red, ali potom ukloni pending i upiše audit; approve još stvori račun. Popravak provjerava izvršitelja nakon UPDLOCK/HOLDLOCK zahtjeva u istoj transakciji. SQL autorizacijska greška pretvara se u UnauthorizedAccessException i handler vraća Forbid. Oracle/Db2 već su provjeravali prava unutar transakcije. Dokaz prije: artifacts/review12-registration-race-before.log.

Posljednji grupni administrator provjeren je na potpuno novim zasebnim Oracle i Db2 shemama TMR_ADMINQA_<12hex>, s jednim početnim administratorom i vlastitim QA grupnim administratorom. Samo u tim shemama privremeno je deaktiviran početni administrator. HR/EN uklanjanje posljednje grupne role ili članstva vratilo je 500, dok su članstvo, rola i efektivna puna administracija ostali očuvani. Finally je ponovno aktivirao početnog administratora. Popravak hvata samo poznatu InvalidOperationException poruku o aktivnom punom administratoru te obrađuje gubitak ovlasti kao Forbid. Ne mijenja samu zaštitu ni backend zaključavanje. Dokazi prije: artifacts/review12-last-admin-oracle-before.log i artifacts/review12-last-admin-db2-before.log.

Oracle R11 baseline potvrdio je gubitak svih pet nacrta: create/edit role te Value/Transition/Catalog tijeka rada. Nazivi/opisi/checkboxi/odabiri vraćali su se na spremljene ili početne vrijednosti, uz neaktivnu zaštitu vraćenog nacrta. Sada se nacrt preuzima nakon provjere prava i vraća samo pripadajućem obrascu/retku. Razor kodira tekst; nepoznati odabir dobiva postojeći HR/EN placeholder, a poznate označene ovlasti ostaju označene. Uspješan redirect odbacuje nacrt. Nevaljani bool parametri i dalje se odbijaju prema R9 pravilima. Dokaz prije: artifacts/review12-role-workflow-baseline.log.

Nove korisničke poruke nisu dodavane; koriste se postojeći HR/EN ključevi. Sheme aplikacijske baze i protokol zaključavanja nisu mijenjani.

## Izolirane provjere

Test-RegistrationRevocationRace.cjs i Invoke-RegistrationRevocationRace.ps1 dopuštaju samo generiranu TeamManager_AccountTest_<guid> bazu. Osam HR/EN slučajeva nakon popravka potvrđuje stvarno čekanje, pending1/accounts0/audit0 i odbijanje odluke nakon opoziva/deaktivacije. Valjani ponovni pokušaj i ponovljena odluka 404 bez novog audita prolaze. Dokaz: artifacts/review12-registration-race-after.log. Ova kontrolirana utrka nije izvođena nad IIS bazom.

Test-LastGroupAdministrator.cjs zahtijeva ignoriranu konfiguraciju s AdminFixture.Isolated=true, TMR_ADMINQA prefiksom i zasebnim localhost portom 7633/7634. Baseline i završni test koriste različite svježe sheme. Nakon popravka sve četiri HR/EN odluke po provideru vraćaju lokaliziranu validaciju uz očuvana prava; axe, finally reaktivacija, naknadno valjano uklanjanje i zabrana pristupa također prolaze. Dokazi: artifacts/review12-last-admin-oracle-after.log i artifacts/review12-last-admin-db2-after.log. Početni administrator vraćen je u aktivno stanje. Sole-admin scenarij nije izvođen na IIS-u niti na zajedničkim demo/preglednim bazama.

New-LastAdministratorFixture.ps1, admin-fixture utility i Start-LastAdministratorPreview.ps1 stvaraju minimalne lokalne sheme s novim testnim administratorom; ne preuzimaju poslovne korisnike/projekte. Privatne konfiguracije, hashovi i artefakti ostaju ignorirani. Postojeće sheme se ne prepisuju. Oracle korisnik mora biti prazan i imati generirani prefiks; Db2 uvijek dobiva novu shemu. Testne aplikacije služe istim paketom preko zasebnih portova.

Test-RoleWorkflowDrafts.cjs provjerava HR/EN sirovi tekst i HTML kodiranje, označene ovlasti i odabire, izolaciju obrazaca, aktivnu zaštitu, otkazanu navigaciju, uspješan oporavak, neovlaštene POST-ove i axe. Koristi samo vlastite QA role i kataloške vrijednosti/prijelaze. Oracle dokaz: artifacts/review12-oracle-drafts.log; SQL dokaz: artifacts/review12-sql-drafts-groups.log; Db2 dokaz: artifacts/review12-db2-drafts.log.

Test-OracleGroups.cjs koristi se i za valjane generičke HR/EN grupne tokove; dodana je provjera HTTP odgovora, obnova izvornog jezika i sigurno prijavljivanje grešaka. Ne uklanja postojeće administratore niti postojeća članstva. Test-Review12Sql.cjs povezuje nacrte i te grupne tokove na privremenoj SQL bazi.

Paket: artifacts/review12-final. Završni IIS prihvat dokumentira se nakon objave. Čisti demo7610 i postojeći pregled7589 nisu korišteni za testne poslovne upise. NVDA, opterećenje i ciljno produkcijsko okruženje nisu ponovno provjeravani; pregled ne potvrđuje odsutnost svih mogućih grešaka.

## Status IIS isporuke

Novi paket još nije objavljen na IIS-u: Windows Start-Process -Verb RunAs završio je porukom „The operation was canceled by the user.” Administratorska skripta nije pokrenuta i manifest artifacts/review12-iis-update.json nije nastao. Na https://localhost:7443 ostaje paket review11-final. Za završni prihvat novog paketa potrebni su ponovljeni potvrđeni Windows UAC zahtjev, nova IIS objava i provjere.

Pripremljeni Test-IisRegistration.cjs i Invoke-IisRegistrationFixture.ps1 prošli su HR/EN na postojećem IIS paketu kao provjera testnog postupka: approve/reject, malformedapprove400 bez promjene, repeat404, jedan audit i axe. Helper se ograničava na TeamManager_IisTest i generirane QA identifikatore te preuzima hash samo od novog QA seed korisnika. Svi stvoreni QA računi deaktivirani su u finally; postojeći administrator i članstva nisu mijenjani. Dokaz: artifacts/review12-iis-registration-baseline.log. To je baseline starog paketa, a ne prihvat nove autorizacijske provjere.

Svi implementacijski popravci imaju prolazne izolirane provjere: registracija na privremenom SQL Serveru, posljednji grupni administrator na svježim Oracle/Db2 shemama, nacrti na SQL Server/Oracle/Db2 te valjani grupni tokovi na privremenom SQL Serveru. IIS prihvat REV12 ostaje otvoren.
