# Ciljni prihvat — nastavak nakon lokalne revizije 13

Datum: 26. 11. 2026. Ovaj plan razdvaja lokalne dokaze od provjera koje zahtijevaju stvarno odredište. Lokalni dokaz nije potvrda bankarske konfiguracije. Nove produkcijske veze niti tajne nisu unesene ovim korakom.

| Backlog | Potrebni ulazi | Kriterij zatvaranja u ciljnom okruženju |
|---|---|---|
| MAIL-01 | Namjenski sandučić, IMAP TLS host/port, način autentikacije, dopušteni projekt i mapiranje pošiljatelja; tajne izvan repozitorija | Kontrolirana poruka stvara točno jedan zadatak; ponavljanje ne duplicira; neovlašteni pošiljatelj ne upisuje; ponovni spoj ne gubi poruke. Slanje testnih poruka dogovoriti zasebno. |
| AUTH-06 | Bankarski OIDC issuer/klijent/callback i mapiranje subjekata, ili LDAPS host/certifikati/DN mapiranje, testni računi | Dopušteni račun se prijavljuje, nepoznat/deaktiviran se odbija; aplikacijske ovlasti i opoziv ostaju učinkoviti. |
| OPS-02 | Windows/IIS verzija, DNS, certifikat, namjenski procesni identitet, SQL prava i trajne putanje | HTTPS/prijava/HR-EN tokovi, restart i trajna sesija prema politici, zaštićena konfiguracija te zdravstvene provjere prolaze na ciljnoj instalaciji. |
| OPS-04 | Klaster, namespace, registry, storage, ruta/certifikat, dopušteni UID i mrežne politike | Objavljena slika radi s dopuštenim neprivilegiranim identitetom; restart/premještanje poda čuva podatke i ključeve; proxy/ruta i readiness rade. |
| OPS-03 | Odgovorna osoba za bazu, backup odredište, retencija, cilj obnove, odvojena baza za probu i nadzor | Izvršena obnova u odvojeno odredište, provjeren sadržaj i povratak kompatibilnog paketa; zabilježeni trajanje, dokazi i postupak oporavka. VERIFYONLY sam nije dokaz obnove. |
| DB-02 (z/OS) | Potvrda da je z/OS potreban, verzija/driver/licenca, izdvojena shema i pristup | Migracije, transakcije, Unicode, ovlasti, konkurentni upisi i aplikacijski tokovi prolaze na toj platformi. Db2 LUW dokaz se ne prenosi automatski na z/OS. |
| QA-02 | Odabrana topologija, reprezentativni anonimni/testni podaci, očekivana istodobnost i dogovoreni pragovi | Funkcionalni i HR/EN pristupačni tokovi te mjerljivo opterećenje prolaze s dogovorenim pragovima na ciljnoj infrastrukturi. |

## Redoslijed

1. Odabrati jedno prvo odredište i provider; lokalni Windows/IIS sa SQL Serverom ima zasebne postojeće dokaze, ali ne određuje izbor banke.
2. Pripremiti izolirano prihvatno okruženje i njegovu konfiguraciju prema [INSTALL.md](INSTALL.md), odnosno [DB2-LOCAL.md](DB2-LOCAL.md).
3. Zabilježiti commit, hash paketa, verzije platforme i sigurnosnu kopiju prije migracije. Zadržati postojeće podatke, konfiguraciju i ključeve izvan zamjenjive aplikacijske mape.
4. Izvesti instalaciju, zdravstvene i funkcionalne provjere te probu povratka u odvojenoj bazi. Testovi koji stvaraju/uklanjaju QA zapise ne pokreću se proizvoljno na poslovnoj bazi.
5. Povezati samo potrebne vanjske integracije i izvesti dogovorene kontrolirane scenarije.
6. U backlog uz svaku zatvorenu stavku dodati stvarni rezultat, opseg i referencu dokaza; paket i dokumentaciju poslati na GitHub bez tajni ili poslovnih podataka.

Bez ciljnih ulaza ove stavke ostaju otvorene. Može se nastaviti lokalnim razvojem ili popravkom reproduciranog nalaza, ali ponavljanje lokalne revizije nije zamjena za ciljni prihvat.