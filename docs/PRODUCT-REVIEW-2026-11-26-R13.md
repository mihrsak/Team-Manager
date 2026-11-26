# Trinaesta revizija Team Managera — 26. 11. 2026.

Revizija je započela na commitu **e71af69** na stvarnom [lokalnom IIS-u](https://localhost:7443). Početni pregled 82 HR/EN prikaza nije našao nove axe prijave, prelijevanje dokumenta ni JavaScript greške. Dodatni scenariji potvrdili su četiri nalaza, dva P1 i dva P2.

| Stavka | Prioritet | Nalaz i popravak |
|---|---|---|
| REV13-01 | P1 | SQL odgovor i moderiranje foruma nakon čekanja ciljnog retka nisu ponovno provjeravali aktualni pristup. Sada se pod zaključavanjem provjeravaju aktivnost, aktivan projekt, vidljivost i efektivna dozvola. |
| REV13-02 | P1 | SQL grupna promjena mogla je izvršiti uklanjanje nakon opoziva ovlasti/deaktivacije. Sada transakcija prvo zaključava cilj, zatim provjerava aktivnog globalnog administratora prije promjene i audita. |
| REV13-03 | P2 | Odbijeno stvaranje grupe ili promjena članstva/role gubili su naziv i ciljni odabir. Sada nacrt ostaje u odgovarajućem obrascu/grupi uz zaštitu odlaska i generički nevaljan odabir. |
| REV13-04 | P2 | Odbijeno stvaranje definicije polja gubilo je naziv, a projektno polje zadatka i managersOnly odabir. Sada naziv i ograničenje ostaju u vraćenom nacrtu, odvojeno od nacrta vrijednosti polja. |

## Reprodukcije i implementacija

Na privremenoj SQL bazi QA koordinator zadržao je UPDLOCK vlastite forumske teme. Dok je zahtjev čekao, uklonjeno je vlastito QA projektno članstvo ili deaktiviran njegov izvršitelj. Izvorni paket u sva četiri HR slučaja ipak je dodao odgovor ili promijenio zaključanost teme. Završni redirect sam po sebi nije dokaz zaštite, pa test provjerava i stvarno odsustvo promjene. Dokaz prije: artifacts/review13-forum-before.log.

SQL forumski upisi sada koriste Serializable transakciju. Stvaranje teme zaključava ciljni projekt, a odgovor/moderiranje ciljnu temu. Provjera iza tog zaključavanja uključuje aktivnost izvršitelja, Active projekt, javnu ili ovlaštenu vidljivost te forum.post/forum.moderate ili *. Prava obuhvaćaju efektivne globalne i naslijeđene projektne role; čitanja autorizacije ostaju zaključana do commita. Zaključana tema i dalje odbija odgovor. Oracle/Db2 već su provjeravali ta prava u svojim transakcijama; njihov backend nije prepisivan.

Drugi SQL koordinator zadržao je vlastiti QA grupni red. Četiri HR slučaja pokazala su uklanjanje običnog članstva ili čitalačke role nakon opoziva administratorske role/deaktivacije. Nisu se dodjeljivale privilegije nakon gubitka prava. Dokaz prije: artifacts/review13-group-race-before.log. SQL grupni zapis sada zaključava naziv ili ciljni member/role red, provjerava aktivnog izvršitelja i globalni * te zadržava provjeru kroz Serializable transakciju. Provjera prethodi DELETE-u: legitimno samo-uklanjanje ostaje dopušteno dok drugi administrator ostaje. Audit se upisuje samo za uspješnu autoriziranu promjenu.

Oracle R12 baseline potvrdio je HR/EN gubitak svih grupa/definicijskih unosa: naziv grupe; cilj korisnika/role; nazive univerzalnih definicija user/project/time; naziv i managersOnly projektne definicije zadatka. Zaštita vraćenog nacrta bila je neaktivna. Dokaz: artifacts/review13-group-fields-baseline.log. Nacrti se sada hvataju nakon provjere prava i prikazuju samo u pogođenom obrascu/retku. Nepostojeći odabiri koriste postojeći generički HR/EN tekst; Razor kodira sirove vrijednosti. Definicijski nacrt ne aktivira postojeći field-value nacrt. Duplikat ne mijenja spremljenu vidljivost, a ispravak može stvoriti ograničeno polje uz očuvan checkbox.

Novi tekstovi sučelja nisu dodavani; koriste se postojeći HR/EN ključevi. Sheme i protokol zaključavanja nisu mijenjani. SQL testni runner dobio je zasebne logove po generiranoj bazi jer su paralelni izolirani testovi dijelili migration/server putanje; to je poboljšanje provjera, ne produktna migracija.

## Izolirane provjere

Test-ForumConsistency.cjs i Invoke-ForumConsistencyRace.ps1 zahtijevaju generiranu TeamManager_AccountTest_<guid> bazu. Osam HR/EN slučajeva (reply/moderation × opoziv članstva/deaktivacija) potvrđuje stvarno čekanje, odbijanje i nepromijenjene podatke nakon popravka. Dokaz: artifacts/review13-sql.log.

Test-GroupRevocationRace.cjs i Invoke-GroupRevocationRace.ps1 koriste samo vlastite QA retke i istu strogu izolaciju baze. Osam HR/EN slučajeva zadržava članstvo/običnu rolu i auditDelta0 nakon odbijanja. Valjani ponovni pokušaj i legitimno samo-uklanjanje oba oblika prolaze. Dokaz: artifacts/review13-group-race-after.log. Ova dva kontrolirana skupa nisu izvođena nad IIS bazom.

Test-ForumPermissions.cjs prošao je HR/EN na SQL Serveru, Oracleu i Db2: stvaranje i odgovor s naslijeđenim ovlastima privatnog djeteta, moderiranje/zaključavanje, odbijen odgovor u zaključanoj temi, javni projekt bez prava pisanja, opoziv hijerarhijskog pristupa bez privatnog sadržaja i axe.

Test-GroupFieldDefinitionDrafts.cjs prošao je HR/EN na sva tri pružatelja: raw input i kodiranje, nepostojeći odabiri, izolirani guardovi, otkazana navigacija, ispravak, definicije user/project/time, managersOnly, vidljivost izvornog javnog polja i skrivenost novog ograničenog polja za Developera te zabrana neovlaštenog/opozvanog POST-a i axe.

Test-Review13Sql.cjs povezuje forumske regresije i nove nacrte s prethodnim Test-FieldBulkDrafts.cjs. Prethodni field-value/skupni sukobi, izvorne revizije, odabiri i atomarni oporavak također prolaze. Funkcionalni dokazi: artifacts/review13-sql.log, artifacts/review13-oracle-test.log i artifacts/review13-db2-test.log. Read-only pregled sourcea nije našao konkretnu regresiju u zadanim scenarijima.

Paket: artifacts/review13-final. Završna IIS objava i prihvat potvrđeni su ispod. NVDA, opterećenje i ciljno produkcijsko okruženje nisu ponovno provjeravani. Demo7610 i postojeći pregled7589 nisu korišteni za testne poslovne upise. Pregled ne potvrđuje odsutnost svih mogućih grešaka.

## Završna IIS objava i prihvat

Nakon Windows UAC potvrde paket je objavljen na https://localhost:7443 iz direktorija C:\inetpub\TeamManager-LocalTest\review-20261126-120549. Očuvani su postojeća testna baza i konfiguracija; verificirani su COPY_ONLY backup, hash paketa i HTTPS readiness 200. Manifest: artifacts/review13-iis-update.json.

Na novom IIS paketu prošle su HR/EN provjere:

- Test-GroupFieldDefinitionDrafts.cjs: grupni i definicijski nacrti, odabiri, oporavak, ograničena vidljivost za Developera, autorizacija i axe. Dokaz: artifacts/review13-iis-drafts.log.
- Test-ForumPermissions.cjs: naslijeđeno stvaranje/odgovor/moderiranje, zaključana tema, javni projekt bez pisanja, opoziv hijerarhije i axe. Dokaz: artifacts/review13-iis-forum.log.
- Test-FieldBulkDrafts.cjs: prethodni nacrti vrijednosti polja, izvorne revizije, ponovljeni sukobi, odabiri/postavke skupnih izmjena i atomarni oporavak. Dokaz: artifacts/review13-iis-field-bulk.log.
- Test-IisLocalAcceptance.cjs: glavni tokovi projekata, zadataka, komentara, vremena, pretrage/CSV, lokalizirani 404, mobilni izvještaj i axe. Dokaz: artifacts/review13-iis-acceptance.log.

Ponovljeni pregled novog IIS paketa prošao je 82 HR/EN prikaza uz očekivane HTTP statuse: nema novih axe prijava, prelijevanja dokumenta ni JavaScript grešaka. Dokazi: artifacts/review13-overview-final.log i artifacts/product-review13-final-2026-11-26/overview.json. To je završni pregled nove objave, odvojen od početnog pregleda prethodnog paketa.

Kontrolirani SQL slučajevi nisu izvođeni nad IIS bazom. IIS prihvat obuhvaća zasebne funkcionalne HR/EN tokove, uz vlastite označene QA zapise. Sva četiri nalaza REV13 zatvorena su u provjerenom lokalnom opsegu.
