# Osma revizija Team Managera — 26. 11. 2026.

Revizija je započela na commitu a383d30 na lokalnom IIS-u https://localhost:7443. Početni pregled 82 HR/EN prikaza nije našao axe prijave, prelijevanje dokumenta ni JavaScript greške. Dodatna provjera odbijenih upisa potvrdila je tri nalaza.

| Stavka | Prioritet | Popravak |
|---|---|---|
| REV8-01 | P1 | Vraćeni nacrt zadatka sada aktivira zaštitu nespremljenih izmjena. Sukob prikazuje aktualne spremljene podatke i zahtijeva potvrdu pregleda prije novog pokušaja. Drugi sukob ponovno odbija upis uz očuvan nacrt i pokušavanu reviziju. |
| REV8-02 | P2 | Odbijeni komentar čuva izvorni tekst, početne prazne retke i zaštitu odlaska; ispravak se može spremiti bez ponovnog pisanja. |
| REV8-03 | P2 | Odbijena privatna bilješka čuva tekst isključivo u svom obrascu, bez prijenosa u javni komentar. |

## Reprodukcija i provjere

`scripts/Test-IssueDraftRecovery.cjs --observe` na izvornom IIS paketu potvrdio je gubitak komentara i privatne bilješke nakon serverskog odbijanja teksta duljeg od 4000 znakova. Namjerno se za taj test uklanja HTML maxlength kako bi se provjerio serverski povrat. Sukob glavnog obrasca nije imao aktivnu zaštitu vraćenog nacrta ni kontrolu pregleda aktualne revizije.

Novi regresijski test koristi vlastite označene projekte i zadatke. Provjerava HR/EN, sirovi tekst i HTML kodiranje, izolaciju javnog i privatnog obrasca, beforeunload zaštitu, uspješan ispravak, dva uzastopna sukoba, izričit pregled revizije i axe na stranici sukoba. Testovi su prošli na izoliranim SQL Server, Oracle i Db2 bazama. SQL/Oracle/Db2 sheme i protokol zaključavanja nisu mijenjani. Autorizacija upisa ostaje u spremištima; vraćanje nacrta glavnog obrasca ponovno dohvaća aktualni zadatak i ovlasti.

Aktualna spremljena revizija koristi se u drugim obrascima (polja i prijenos), odvojeno od revizije odbijenog glavnog nacrta. Ponovno dohvaćanje ne gomila projektne izbore. Nedostupan nositelj/stavka plana ima generičku HR/EN oznaku bez otkrivanja naziva; poslužitelj i dalje provjerava dopuštenost odabira.

Paket: `artifacts/review8-final`. Izvorni pregled: `artifacts/product-review8-2026-11-26/overview.json`. Rezultati izoliranih testova: `artifacts/review8-sql.log`, `artifacts/review8-oracle-test.log`, `artifacts/review8-db2-test.log`. Lokalni artefakti nisu u Gitu.

Ovaj pregled ne potvrđuje odsutnost svih grešaka. NVDA, test opterećenja i ciljno produkcijsko okruženje nisu ponovno provjeravani u ovoj reviziji.

## Status IIS objave

Objava novog paketa nije izvršena: Windows Start-Process -Verb RunAs završio je porukom „The operation was canceled by the user.” Administratorska skripta nije pokrenuta i manifest objave nije nastao. Na https://localhost:7443 ostaje prethodni paket review7-final. Za završnu IIS provjeru potreban je uspješan Windows UAC korak, zatim Test-IssueDraftRecovery.cjs, Test-IisLocalAcceptance.cjs i ponovljeni HR/EN pregled. Osnovni pregled 82 prikaza odnosi se na postojeći IIS paket, ne na novu objavu. REV8 popravci potvrđeni su na izoliranim bazama; status objave i završnog IIS prihvata ostaje otvoren.

Dodatna HR/EN regresija `Test-FieldBulkDrafts.cjs` na izoliranom Db2 prošla je: polja, izvorne revizije, ponovljeni sukobi, odabir/postavke skupnih izmjena, atomarni oporavak i opoziv ovlasti. Rezultat: `artifacts/review8-field-bulk.log`.
