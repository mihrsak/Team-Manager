# Završni lokalni prihvat — 24. 11. 2026.

**Status: PROŠAO u navedenom lokalnom opsegu.** Ovo je automatski sljedeći korak nakon zatvaranja REV3-05/06. Svih šest nalaza treće revizije ima implementaciju i dokaz provjere; nema novog blokirajućeg nalaza u provjerenim scenarijima.

Provjereni kod: `10c8943`. Paket: `artifacts/rev3-navigation-complete`, .NET 10. Demo: [https://localhost:7610](https://localhost:7610), postojeći radni pregled: [https://localhost:7589](https://localhost:7589). Oba su lokalni dotnet HTTPS procesi s postojećom Db2 konfiguracijom, relacijskom shemom 11 i protokolom zaključavanja 5. Nije provedena migracija niti ponovno stvaranje korisničke baze.

SHA-256 datoteke `TeamManager.Web.dll`:

```text
7EE177189602AD21431C8D9954B0A74E8CB7E1C623D6DAEBC2509184743725B9
```

## Kriteriji i rezultat

| Područje | Rezultat i dokaz |
|---|---|
| REV3-01 — nacrti | HR/EN odbijeni forum, novost i plan na stvarnom demou čuvaju tekst i izvornu reviziju. U privremenoj SQL bazi prolaze stvarna spremanja, duplikati, nevaljan datum, početni prazni retci i dvije uzastopne konkurentne izmjene plana s izričitim pregledom. `Test-CollaborationDrafts.cjs`. |
| REV3-02 — mobilne tablice | Izravno izmjereni HR/EN stupci imaju najmanje 96 px umjesto prelamanja u pojedinačne znakove. Dostupni su lokalni okvir, fokus i pomicanje tipkovnicom; ranije ciljane provjere istog rješenja uključuju 320/390 px i obje teme na sva tri providera. Aktualna provjera demoa potvrđuje širine šest skupina prikaza. |
| REV3-03 — HTTP statusi | Autentificirani nevaljani kalendar/Gantt/forum vraćaju 400. Nepostojeća stranica ostaje generički 404. Ranije provjereni middleware čuva ostale statuse, tijelo i Retry-After; ova provjera nije izazivala opterećenje ili kvar servisa. |
| REV3-04 — povratni kontekst | Stvarni demo vraća u izvorni mjesec/projekt/sidro kalendara, Gantta i aktivnosti te iz vremena u izvorni zadatak. U privremenoj SQL bazi prolaze pretraga kroz osam modula, druga stranica izvještaja, valjan/nevaljan upis i opoziv ovlasti. `Test-ReturnContext.cjs`. |
| REV3-05 — grupa nositelja | Na demou i izoliranim SQL Server/Oracle/Db2 bazama HR prikazuje hrvatsku, EN englesku sistemsku oznaku. Stvarna prikazna imena ostaju nepromijenjena, uključujući testno ime s riječima Unassigned i Nije dodijeljen. |
| REV3-06 — navigacija | Na demou prolazi 18 HR/EN scenarija glavne/administrativne navigacije. Ciljani `Test-LocalizedNavigation.cjs` prolazi na SQL Serveru, Oracleu i Db2 sa i bez JavaScripta, uključujući vlasnika i vrstu polja, vlastiti/tuđi profil, 404, odbijen pristup i opoziv članstva. |
| Ranije zaštite nacrta | Aktualni paket prolazi `Test-DraftProtection.cjs` u privremenoj SQL bazi: wiki konflikti i eksplicitni pregled, vraćeni nacrti vremena, stvarna preuzimanja dokumenata/privitaka/CSV-a te odustajanje od odlaska i reloada. |
| Glavni demo i ovlasti | `Test-ProductDemo.cjs` prolazi 28 HR/EN axe provjera i granice privatnosti; `Test-ReviewSmoke.cjs` potvrđuje postojeće Db2 podatke bez poslovnih upisa. Readiness oba URL-a je 200. |

Završna kontrola sadržaja nakon svih odbijenih demo unosa potvrđuje **tri projekta, osam zadataka, tri unosa vremena u projektu portala, wiki reviziju 2, jednu temu foruma, jednu novost i nepromijenjene revizije dvije stavke plana**. Jezik i tema ostali su na početnim postavkama. Pozitivni regresijski upisi pripadaju isključivo izoliranim testnim bazama.

Automatizirane axe provjere nisu formalna potvrda potpune WCAG usklađenosti. U ovom koraku nisu ponovno izvršeni ručni NVDA, opterećenje, obnova baze ili vanjske integracije; povijesni dokazi tih područja ostaju odvojeni u statusu izdanja.

## Preostale ciljne provjere iz osnovnog backloga

| Stavka | Što još treba potvrditi na ciljnom okruženju |
|---|---|
| AUTH-06 | Bankarski OIDC/SSO i AD/LDAPS: stvarni identiteti, mapiranje računa, prijava, odjava i opoziv pristupa. Lokalni Keycloak/OpenLDAP dokaz ostaje zaseban. |
| MAIL-01 | Namjenski IMAP sandučić i odobrena SMTP konfiguracija: stvarna dolazna poruka, stvaranje zadatka, deduplikacija, isporuka i pogreške. |
| OPS-02 / OPS-03 | Ciljni IIS, HTTPS i servisni identitet, konfiguracija baze/tajni, nadzor, backup/restore i rollback upravo odabranog paketa. Ovaj prihvat koristi dotnet pregled. |
| OPS-04 | Ciljni OpenShift projekt, registry, Secreti, mrežne i sigurnosne politike, ruta/proxy te trajnost sesije i podataka u tom klasteru. |
| DB-02 | Db2 z/OS s odgovarajućim pružateljem/licencom i ciljnom bazom. Lokalni Db2 LUW nije dokaz za z/OS. |
| QA-02 | Kapacitet, pristupačnost i konfiguracija konačnog produkcijskog okruženja. Povijesni lokalni IIS/NVDA rezultati ne zamjenjuju tu provjeru. |

Ovi vanjski uvjeti nisu zatvoreni lokalnim prihvatom niti pushom na GitHub. Sljedeći provedivi korak za produkcijski prihvat jest odabrano ciljno okruženje s pripadajućim pristupom i konfiguracijom.

## Ponovljivost

Za čisti demo koristiti postupak i postojeći manifest iz [PRODUCT-DEMO.md](PRODUCT-DEMO.md). `Test-LocalizedNavigation.cjs`, `Test-CollaborationDrafts.cjs`, `Test-ReturnContext.cjs` i `Test-DraftProtection.cjs` stvaraju podatke; pokretati ih preko `Test-AccountSql.ps1` ili na izoliranim Oracle/Db2 shemama, nikada nad korisničkim radnim podacima.

Lokalni dokazi: ignorirana mapa `artifacts/local-acceptance-2026-11-24` (`additional.json`, `navigation.json`, HR/EN snimke), dnevnici `artifacts/local-acceptance-*.log` i `artifacts/rev3-navigation-*.log`. Izvještaj ne sadrži tajne, pristupne datoteke ni snimku baze.
